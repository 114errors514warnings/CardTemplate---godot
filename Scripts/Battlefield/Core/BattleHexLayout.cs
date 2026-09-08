using System;
using System.Collections.Generic;

namespace CardSimulator.Battlefield;

/// <summary>Pointy-top, edge-to-edge hexes. Independent of the adventure-map layout.</summary>
public static class BattleHexLayout
{
    private static readonly AxialHex[] offsets =
    {
        new(1, 0), new(1, -1), new(0, -1), new(-1, 0), new(-1, 1), new(0, 1)
    };

    public static IEnumerable<AxialHex> Neighbors(AxialHex cell)
    {
        foreach (var offset in offsets) yield return new AxialHex(cell.Q + offset.Q, cell.R + offset.R);
    }

    public static (double X, double Y) Center(AxialHex cell, double radius)
    {
        ValidateRadius(radius);
        return (Math.Sqrt(3) * radius * (cell.Q + cell.R / 2d), 1.5 * radius * cell.R);
    }

    public static AxialHex Pick(double x, double y, double radius)
    {
        ValidateRadius(radius);
        double q = (Math.Sqrt(3) * x / 3 - y / 3) / radius;
        double r = 2 * y / (3 * radius), s = -q - r;
        int iq = (int)Math.Round(q), ir = (int)Math.Round(r), @is = (int)Math.Round(s);
        double dq = Math.Abs(iq - q), dr = Math.Abs(ir - r), ds = Math.Abs(@is - s);
        if (dq >= dr && dq >= ds) iq = -ir - @is;
        else if (dr >= ds) ir = -iq - @is;
        return new AxialHex(iq, ir);
    }

    private static void ValidateRadius(double radius)
    {
        if (!double.IsFinite(radius) || radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
    }
}
