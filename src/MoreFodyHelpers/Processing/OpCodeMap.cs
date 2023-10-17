﻿using System.Reflection;

namespace MoreFodyHelpers.Processing;

public static class OpCodeMap
{
    private static readonly Dictionary<string, OpCode> _opCodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
                                                                                 .Where(field => field.IsInitOnly && field.FieldType == typeof(OpCode))
                                                                                 .ToDictionary(field => field.Name, field => (OpCode)field.GetValue(null));

    public static OpCode FromCecilFieldName(string fieldName)
    {
        if (!_opCodes.TryGetValue(fieldName, out var opCode))
            throw new WeavingException($"Unknown opcode: {fieldName}");

        return opCode;
    }
}
