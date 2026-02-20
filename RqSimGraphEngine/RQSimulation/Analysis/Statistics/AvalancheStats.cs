using System;

namespace RQSimulation
{
    public static class AvalancheStats
    {
        public static (double alpha, double offset) FitPowerLaw(ReadOnlySpan<double> sizes)
        {
            // лог-лог регрессия без подгонки под внешние данные, только по модели
            int n = sizes.Length;
            if (n == 0) return (0.0, 0.0);

            double sx = 0.0, sy = 0.0, sxx = 0.0, sxy = 0.0;
            for (int i = 0; i < n; i++)
            {
                if (sizes[i] <= 0) continue;
                double x = Math.Log(sizes[i]);
                double y = Math.Log(1.0 / n); // частота ~ 1/n на бин, упрощённо
                sx += x; sy += y; sxx += x * x; sxy += x * y;
            }
            double denom = n * sxx - sx * sx;
            if (Math.Abs(denom) < 1e-12) return (0.0, 0.0);
            double a = (n * sxy - sx * sy) / denom;
            double b = (sy - a * sx) / n;
            return (a, b);
        }
    }
}
