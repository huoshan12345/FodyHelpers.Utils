﻿namespace MoreFodyHelpers.Building;

public class FieldRefBuilder
{
    private readonly FieldReference _field;

    public FieldRefBuilder(ModuleWeavingContext context, TypeReference typeRef, string fieldName)
    {
        var typeDef = typeRef.ResolveRequiredType(context);
        var fields = typeDef.Fields.Where(f => f.Name == fieldName).ToList();

        _field = fields switch
        {
        [var field] => context.Module.ImportReference(field.Clone()),
        [] => throw new WeavingException($"Field '{fieldName}' not found in type {typeDef.FullName}"),
            _ => throw new WeavingException($"Ambiguous field '{fieldName}' in type {typeDef.FullName}")
        };

        _field.DeclaringType = typeRef;
    }

    public FieldReference Build()
        => _field;
}
