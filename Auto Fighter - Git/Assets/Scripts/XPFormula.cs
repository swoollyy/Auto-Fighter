using System;
using UnityEngine;

public static class XPFormula
{
    // Tunables
    public static double A = 10.0;   // base; sets Lv1->2 XP
    public static double p = 1.3;    // base growth
    public static double bumpEvery = 5.0;
    public static double bumpWidth = 0.8;    // narrow = "brick wall"
    public static double bumpHeight = 0.20;  // 0.40 => +40% at the wall center

    // Logistic hump centered at `center`
    static double Bump(double n, double center, double width, double height)
    {
        // bounds/guard: avoid division by ~0 if someone sets width too tiny
        width = Math.Max(0.5, width);

        double x = (n - center) / width;
        double s = 1.0 / (1.0 + Math.Exp(-x));        // 0..1 sigmoid
        double hump = s * (1.0 - s) * 4.0;            // 0..1 bell, peak at center
        return 1.0 + height * hump;                   // 1.0 away from center, 1+height at center
    }

    // XP required to go from level n -> n+1; n >= 1
    public static int XpReq(int n)
    {
        double baseReq = A * Math.Pow(n, p);

        // With narrow width, only the nearest centers meaningfully affect n.
        // We multiply the nearest three (center, +/- 1 period) for clean tails.
        int center = (int)Math.Round(n / bumpEvery) * (int)bumpEvery;

        double mul =
            Bump(n, center - bumpEvery, bumpWidth, bumpHeight) *
            Bump(n, center, bumpWidth, bumpHeight) *
            Bump(n, center + bumpEvery, bumpWidth, bumpHeight);

        // Round to integer for final result
        return Mathf.Max(1, (int)Math.Round(baseReq * mul));
    }
}