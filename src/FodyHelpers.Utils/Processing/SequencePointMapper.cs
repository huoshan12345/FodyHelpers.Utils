﻿namespace FodyHelpers.Utils.Processing;

public class SequencePointMapper
{
    private readonly MethodDefinition _method;
    private readonly bool _generateSequencePoints;
    private readonly List<SequencePoint> _inputSequencePoints;
    private readonly Dictionary<SequencePoint, Instruction> _outputSequencePointMap = new();

    public SequencePointMapper(MethodDefinition method, bool generateSequencePoints)
    {
        _method = method;
        _generateSequencePoints = generateSequencePoints;
        _inputSequencePoints = _method.DebugInformation.HasSequencePoints ? _method.DebugInformation.SequencePoints.ToList() : new();
    }

    public SequencePoint? GetInputSequencePoint(Instruction? instruction)
    {
        if (instruction == null)
            return null;

        return _inputSequencePoints.LastOrDefault(sp => sp.Offset <= instruction.Offset);
    }

    public SequencePoint? MapSequencePoint(Instruction? inputInstruction, Instruction? outputInstruction)
    {
        if (inputInstruction == null || outputInstruction == null)
            return null;

        if (!_generateSequencePoints)
            return null;

        var inputSequencePoint = GetInputSequencePoint(inputInstruction);
        if (inputSequencePoint == null)
            return null;

        var sequencePoints = _method.DebugInformation.SequencePoints;

        var indexOfOutputSequencePoint = sequencePoints.IndexOfFirst(i => i.Offset > inputSequencePoint.Offset);
        if (indexOfOutputSequencePoint < 0)
            indexOfOutputSequencePoint = sequencePoints.Count;

        var outputSequencePoint = new SequencePoint(outputInstruction, inputSequencePoint.Document)
        {
            StartLine = inputSequencePoint.StartLine,
            StartColumn = inputSequencePoint.StartColumn,
            EndLine = inputSequencePoint.EndLine,
            EndColumn = inputSequencePoint.EndColumn
        };

        sequencePoints.Insert(indexOfOutputSequencePoint, outputSequencePoint);
        _outputSequencePointMap[outputSequencePoint] = outputInstruction;

        return outputSequencePoint;
    }

    public void MergeWithPreviousSequencePoint(SequencePoint? sequencePoint)
    {
        if (sequencePoint == null || !_method.DebugInformation.HasSequencePoints)
            return;

        var sequencePoints = _method.DebugInformation.SequencePoints;

        var index = sequencePoints.IndexOf(sequencePoint);
        if (index <= 0)
            return;

        var previousSequencePoint = sequencePoints[index - 1];
        if (previousSequencePoint.Document.Url != sequencePoint.Document.Url)
            return;

        previousSequencePoint.EndLine = sequencePoint.EndLine;
        previousSequencePoint.EndColumn = sequencePoint.EndColumn;

        sequencePoints.RemoveAt(index);
    }

    public void PostProcess()
    {
        if (!_generateSequencePoints)
            return;

        RemoveObsoleteSequencePoints();
    }

    private void RemoveObsoleteSequencePoints()
    {
        if (!_method.DebugInformation.HasSequencePoints)
            return;

        var sequencePoints = _method.DebugInformation.SequencePoints;
        HashSet<Instruction>? instructions = null;

        for (var i = sequencePoints.Count - 1; i >= 0; --i)
        {
            var sequencePoint = sequencePoints[i];

            if (!_outputSequencePointMap.TryGetValue(sequencePoint, out var instruction))
                continue;

            instructions ??= _method.Body.Instructions.ToHashSet();

            if (!instructions.Contains(instruction))
                sequencePoints.RemoveAt(i);
        }
    }
}
