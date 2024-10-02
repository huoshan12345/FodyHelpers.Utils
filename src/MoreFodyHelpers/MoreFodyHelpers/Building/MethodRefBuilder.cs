﻿namespace MoreFodyHelpers.Building;

public class MethodRefBuilder
{
    private readonly ModuleWeavingContext _context;
    private MethodReference _method;

    public MethodRefBuilder(ModuleWeavingContext context, TypeReference typeRef, MethodReference method)
    {
        _context = context;

        method = method.TryMapToScope(typeRef.Scope, context.Module);
        _method = _context.Module.ImportReference(_context.Module.ImportReference(method).MakeGeneric(typeRef));
    }

    public MethodRefBuilder(ModuleWeavingContext context, MethodReference method)
    {
        _context = context;
        _method = method;
    }

    public static MethodRefBuilder MethodByName(ModuleWeavingContext context, TypeReference typeRef, string methodName)
        => new(context, typeRef, FindMethod(context, typeRef, methodName, null, null, null));

    public static MethodRefBuilder MethodByNameAndSignature(ModuleWeavingContext context, TypeReference typeRef, string methodName, int? genericArity, TypeReference? returnType, IReadOnlyList<TypeRefBuilder> paramTypes)
        => MethodByNameAndSignature(context, typeRef, methodName, genericArity, returnType?.ToTypeRefBuilder(context), paramTypes);

    public static MethodRefBuilder MethodByNameAndSignature(ModuleWeavingContext context, TypeReference typeRef, string methodName, int? genericArity, TypeRefBuilder? returnType, IReadOnlyList<TypeRefBuilder> paramTypes)
        => new(context, typeRef, FindMethod(context, typeRef, methodName, genericArity, returnType, paramTypes ?? throw new ArgumentNullException(nameof(paramTypes))));

    public static MethodRefBuilder MethodFromDelegateReference(ModuleWeavingContext context, MethodReference methodRef)
    {
        // You can't reason about compiler-generated methods for several reasons:
        // - What appears to be a static method may be changed to an instance method by the compiler,
        //   as calling instance methods through delegates doesn't require their arguments to be shuffled.
        // - A closure could be passed to this method, which changes the way the surrounding code is structured,
        //   in order to enable capturing local variables.
        // - Codegen could differ between debug and release builds.
        // - These implementation details may change at any time.

        if (methodRef.Name.StartsWith("<", StringComparison.Ordinal))
            throw new WeavingException("A compiler-generated method is referenced by the delegate");

        return new MethodRefBuilder(context, methodRef);
    }

    private static MethodDefinition FindMethod(ModuleWeavingContext context, TypeReference typeRef, string methodName, int? genericArity, TypeRefBuilder? returnType, IReadOnlyList<TypeRefBuilder>? paramTypes)
    {
        var typeDef = typeRef.ResolveRequiredType(context);

        var methods = typeDef.Methods.Where(m => m.Name == methodName);

        if (genericArity != null)
        {
            methods = genericArity == 0
                ? methods.Where(m => !m.HasGenericParameters)
                : methods.Where(m => m.HasGenericParameters && m.GenericParameters.Count == genericArity);
        }

        if (returnType != null)
            methods = methods.Where(m => m.ReturnType.FullName == returnType.TryBuild(m)?.FullName);

        if (paramTypes != null)
            methods = methods.Where(m => SignatureMatches(m, paramTypes));

        return methods.ToList() switch
        {
        [var method] => method,
        [] => throw new WeavingException($"Method {GetDisplaySignature(methodName, genericArity, returnType, paramTypes)} not found in type {typeDef.FullName}"),
            _ => throw new WeavingException($"Ambiguous method {GetDisplaySignature(methodName, genericArity, returnType, paramTypes)} in type {typeDef.FullName}")
        };
    }

    private static bool SignatureMatches(MethodReference method, IReadOnlyList<TypeRefBuilder> paramTypes)
    {
        if (method.Parameters.Count != paramTypes.Count)
            return false;

        for (var i = 0; i < paramTypes.Count; ++i)
        {
            var paramType = paramTypes[i].TryBuild(method);
            if (paramType == null)
                return false;

            if (method.Parameters[i].ParameterType.FullName != paramType.FullName)
                return false;
        }

        return true;
    }

    private static string GetDisplaySignature(string methodName, int? genericArity, TypeRefBuilder? returnType, IReadOnlyList<TypeRefBuilder>? paramTypes)
    {
        if (genericArity is null && returnType is null && paramTypes is null)
            return $"'{methodName}'";

        var sb = new StringBuilder();

        if (returnType != null)
            sb.Append(returnType.GetDisplayName()).Append(' ');

        sb.Append(methodName);

        switch (genericArity)
        {
            case 0:
            case null:
                break;

            case 1:
                sb.Append("<T>");
                break;

            default:
                sb.Append('<');

                for (var i = 0; i < genericArity.GetValueOrDefault(); ++i)
                {
                    if (i != 0)
                        sb.Append(", ");

                    sb.Append('T').Append(i);
                }

                sb.Append('>');
                break;
        }

        if (paramTypes != null)
        {
            sb.Append('(');

            for (var i = 0; i < paramTypes.Count; ++i)
            {
                if (i != 0)
                    sb.Append(", ");

                sb.Append(paramTypes[i].GetDisplayName());
            }

            sb.Append(')');
        }

        return sb.ToString();
    }

    public static MethodRefBuilder PropertyGet(ModuleWeavingContext context, TypeReference typeRef, string propertyName)
    {
        var property = FindProperty(context, typeRef, propertyName);

        if (property.GetMethod == null)
            throw new WeavingException($"Property '{propertyName}' in type {typeRef.FullName} has no getter");

        return new MethodRefBuilder(context, typeRef, property.GetMethod);
    }

    public static MethodRefBuilder PropertySet(ModuleWeavingContext context, TypeReference typeRef, string propertyName)
    {
        var property = FindProperty(context, typeRef, propertyName);

        if (property.SetMethod == null)
            throw new WeavingException($"Property '{propertyName}' in type {typeRef.FullName} has no setter");

        return new MethodRefBuilder(context, typeRef, property.SetMethod);
    }

    private static PropertyDefinition FindProperty(ModuleWeavingContext context, TypeReference typeRef, string propertyName)
    {
        var typeDef = typeRef.ResolveRequiredType(context);

        var properties = typeDef.Properties.Where(p => p.Name == propertyName).ToList();

        return properties switch
        {
        [var property] => property,
        [] => throw new WeavingException($"Property '{propertyName}' not found in type {typeDef.FullName}"),
            _ => throw new WeavingException($"Ambiguous property '{propertyName}' in type {typeDef.FullName}")
        };
    }

    public static MethodRefBuilder EventAdd(ModuleWeavingContext context, TypeReference typeRef, string eventName)
    {
        var property = FindEvent(context, typeRef, eventName);

        if (property.AddMethod == null)
            throw new WeavingException($"Event '{eventName}' in type {typeRef.FullName} has no add method");

        return new MethodRefBuilder(context, typeRef, property.AddMethod);
    }

    public static MethodRefBuilder EventRemove(ModuleWeavingContext context, TypeReference typeRef, string eventName)
    {
        var property = FindEvent(context, typeRef, eventName);

        if (property.RemoveMethod == null)
            throw new WeavingException($"Event '{eventName}' in type {typeRef.FullName} has no remove method");

        return new MethodRefBuilder(context, typeRef, property.RemoveMethod);
    }

    public static MethodRefBuilder EventRaise(ModuleWeavingContext context, TypeReference typeRef, string eventName)
    {
        var property = FindEvent(context, typeRef, eventName);

        if (property.InvokeMethod == null)
            throw new WeavingException($"Event '{eventName}' in type {typeRef.FullName} has no raise method");

        return new MethodRefBuilder(context, typeRef, property.InvokeMethod);
    }

    private static EventDefinition FindEvent(ModuleWeavingContext context, TypeReference typeRef, string eventName)
    {
        var typeDef = typeRef.ResolveRequiredType(context);

        var events = typeDef.Events.Where(e => e.Name == eventName).ToList();

        return events switch
        {
        [var evt] => evt,
        [] => throw new WeavingException($"Event '{eventName}' not found in type {typeDef.FullName}"),
            _ => throw new WeavingException($"Ambiguous event '{eventName}' in type {typeDef.FullName}")
        };
    }

    public static MethodRefBuilder Constructor(ModuleWeavingContext context, TypeReference typeRef, IReadOnlyList<TypeRefBuilder> paramTypes)
    {
        var typeDef = typeRef.ResolveRequiredType(context);

        var constructors = typeDef.GetConstructors()
                                  .Where(i => !i.IsStatic && i.Name == ".ctor" && SignatureMatches(i, paramTypes))
                                  .ToList();

        if (constructors.Count == 1)
            return new MethodRefBuilder(context, typeRef, constructors.Single());

        if (paramTypes.Count == 0)
            throw new WeavingException($"Type {typeDef.FullName} has no default constructor");

        throw new WeavingException($"Type {typeDef.FullName} has no constructor with signature ({string.Join(", ", paramTypes.Select(p => p.GetDisplayName()))})");
    }

    public static MethodRefBuilder TypeInitializer(ModuleWeavingContext context, TypeReference typeRef)
    {
        var typeDef = typeRef.ResolveRequiredType(context);

        var initializers = typeDef.GetConstructors()
                                  .Where(i => i.IsStatic && i.Name == ".cctor" && i.Parameters.Count == 0)
                                  .ToList();

        return initializers switch
        {
        [var initializer] => new MethodRefBuilder(context, typeRef, initializer),
        [] => throw new WeavingException($"Type {typeDef.FullName} has no type initializer"),
            _ => throw new WeavingException($"Type {typeDef.FullName} has multiple type initializers")
        };
    }

    public static MethodRefBuilder Operator(ModuleWeavingContext context, TypeReference typeRef, UnaryOperator opType)
    {
        var typeDef = typeRef.ResolveRequiredType(context);
        var memberName = $"op_{opType}";

        var operators = typeDef.Methods
                               .Where(m => m.IsStatic && m.IsSpecialName && m.Name == memberName && m.Parameters.Count == 1)
                               .ToList();

        return operators switch
        {
        [var op] => new MethodRefBuilder(context, typeRef, op),
        [] => throw new WeavingException($"Unary operator {memberName} not found in type {typeDef.FullName}"),
            _ => throw new WeavingException($"Ambiguous operator {memberName} in type {typeDef.FullName}")
        };
    }

    public static MethodRefBuilder Operator(ModuleWeavingContext context, TypeReference typeRef, BinaryOperator opType, TypeRefBuilder leftOperandType, TypeRefBuilder rightOperandType)
    {
        var typeDef = typeRef.ResolveRequiredType(context);
        var memberName = $"op_{opType}";
        var signature = new[] { leftOperandType, rightOperandType };

        var operators = typeDef.Methods
                               .Where(m => m.IsStatic && m.IsSpecialName && m.Name == memberName && SignatureMatches(m, signature))
                               .ToList();

        return operators switch
        {
        [var op] => new MethodRefBuilder(context, typeRef, op),
        [] => throw new WeavingException($"Binary operator {memberName}({leftOperandType.GetDisplayName()}, {rightOperandType.GetDisplayName()}) not found in type {typeDef.FullName}"),
            _ => throw new WeavingException($"Ambiguous operator {memberName} in type {typeDef.FullName}")
        };
    }

    public static MethodRefBuilder Operator(ModuleWeavingContext context, TypeReference typeRef, ConversionOperator opType, ConversionDirection direction, TypeRefBuilder otherType)
    {
        var typeDef = typeRef.ResolveRequiredType(context);
        var memberName = $"op_{opType}";

        var methods = typeDef.Methods.Where(m => m.IsStatic && m.IsSpecialName && m.Name == memberName && m.Parameters.Count == 1);

        var operators = direction switch
        {
            ConversionDirection.From => methods.Where(m => m.Parameters[0].ParameterType.FullName == otherType.TryBuild(m)?.FullName).ToList(),
            ConversionDirection.To => methods.Where(m => m.ReturnType.FullName == otherType.TryBuild(m)?.FullName).ToList(),
            _ => throw new ArgumentOutOfRangeException(nameof(direction))
        };

        return operators switch
        {
        [var op] => new MethodRefBuilder(context, typeRef, op),
        [] => direction switch
        {
            ConversionDirection.From => throw new WeavingException($"Conversion operator {memberName} from {otherType.GetDisplayName()} not found in type {typeDef.FullName}"),
            ConversionDirection.To => throw new WeavingException($"Conversion operator {memberName} to {otherType.GetDisplayName()} not found in type {typeDef.FullName}"),
            _ => throw new ArgumentOutOfRangeException(nameof(direction))
        },
            _ => throw new WeavingException($"Ambiguous operator {memberName} in type {typeDef.FullName}")
        };
    }

    public MethodReference Build()
        => _method;

    public void MakeGenericMethod(TypeReference[] genericArgs)
    {
        if (!_method.HasGenericParameters)
            throw new WeavingException($"Not a generic method: {_method.FullName}");

        if (genericArgs.Length == 0)
            throw new WeavingException("No generic arguments supplied");

        if (_method.GenericParameters.Count != genericArgs.Length)
            throw new WeavingException($"Incorrect number of generic arguments supplied for method {_method.FullName} - expected {_method.GenericParameters.Count}, but got {genericArgs.Length}");

        var genericInstance = new GenericInstanceMethod(_method);
        genericInstance.GenericArguments.AddRange(genericArgs);

        _method = _context.Module.ImportReference(genericInstance);
    }

    public void SetOptionalParameters(TypeReference[] optionalParamTypes)
    {
        if (_method.CallingConvention != MethodCallingConvention.VarArg)
            throw new WeavingException($"Not a vararg method: {_method.FullName}");

        if (_method.Parameters.Any(p => p.ParameterType.IsSentinel))
            throw new WeavingException("Optional parameters for vararg call have already been supplied");

        if (optionalParamTypes.Length == 0)
            throw new WeavingException("No optional parameter type supplied for vararg method call");

        var methodRef = _method.Clone();
        methodRef.CallingConvention = MethodCallingConvention.VarArg;

        for (var i = 0; i < optionalParamTypes.Length; ++i)
        {
            var paramType = optionalParamTypes[i];
            if (i == 0)
                paramType = paramType.MakeSentinelType();

            methodRef.Parameters.Add(new ParameterDefinition(paramType));
        }

        _method = _context.Module.ImportReference(methodRef);
    }

    public override string ToString()
        => _method.ToString();
}
