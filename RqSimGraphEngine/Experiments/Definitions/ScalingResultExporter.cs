using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace RqSimGraphEngine.Experiments.Definitions;

/// <summary>
/// Exports and analyzes vacuum scaling experiment results.
///
/// Provides:
/// 1. CSV export (for spreadsheet tools)
/// 2. JSON export (for programmatic analysis)
/// 3. Log-log linear regression → slope α
/// 4. Human-readable summary report
///
/// The key scientific output is the slope α from:
///   log₁₀(⟨ε_vac⟩) = α · log₁₀(N) + β
///
/// Interpretation:
///   α ≈  0   → vacuum catastrophe not resolved
///   α ≈ −0.5 → partial dilution (1/√N)
///   α ≈ −1   → full dilution (1/N) — publishable result
/// </summary>
public static class ScalingResultExporter
{
    // ============================================================
    // CSV / JSON export
    // ============================================================

    /// <summary>
    /// Exports data points to a CSV file with invariant-culture formatting.
    /// </summary>
    public static void ExportCsv(IReadOnlyList<ScalingDataPoint> data, string path)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var sb = new StringBuilder();
        sb.AppendLine("N,VacuumNodeCount,AvgVacuumEnergy,EnergyVariance,SpectralDimension,AvgRicciCurvature,ThermalizationStep,WallClockSeconds");

        foreach (ScalingDataPoint p in data)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{p.N},");
            sb.Append(CultureInfo.InvariantCulture, $"{p.VacuumNodeCount},");
            sb.Append(CultureInfo.InvariantCulture, $"{p.AvgVacuumEnergy:G6},");
            sb.Append(CultureInfo.InvariantCulture, $"{p.EnergyVariance:G6},");
            sb.Append(CultureInfo.InvariantCulture, $"{p.SpectralDimension:F4},");
            sb.Append(CultureInfo.InvariantCulture, $"{p.AvgRicciCurvature:G6},");
            sb.Append(CultureInfo.InvariantCulture, $"{p.ThermalizationStep},");
            sb.AppendLine(p.WallClockSeconds.ToString("F2", CultureInfo.InvariantCulture));
        }

        File.WriteAllText(path, sb.ToString());
    }

    /// <summary>
    /// Exports data points to a pretty-printed JSON file.
    /// </summary>
    public static void ExportJson(IReadOnlyList<ScalingDataPoint> data, string path)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(data, options);
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Exports a full analysis report (CSV + JSON + summary text) to a directory.
    /// Returns the paths of all created files.
    /// </summary>
    public static string[] ExportFullReport(
        IReadOnlyList<ScalingDataPoint> data,
        string directory,
        string? timestampOverride = null)
    {
        ArgumentNullException.ThrowIfNull(data);

        Directory.CreateDirectory(directory);

        string ts = timestampOverride
            ?? DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

        string csvPath = Path.Combine(directory, $"vacuum_scaling_{ts}.csv");
        string jsonPath = Path.Combine(directory, $"vacuum_scaling_{ts}.json");
        string reportPath = Path.Combine(directory, $"vacuum_scaling_{ts}_report.txt");

        ExportCsv(data, csvPath);
        ExportJson(data, jsonPath);

        string report = GenerateSummaryReport(data);
        File.WriteAllText(reportPath, report);

        return [csvPath, jsonPath, reportPath];
    }

    // ============================================================
    // Log-Log regression
    // ============================================================

    /// <summary>
    /// Result of a log-log linear regression on scaling data.
    /// </summary>
    /// <param name="Alpha">Scaling exponent (slope in log-log space)</param>
    /// <param name="Beta">Intercept in log-log space</param>
    /// <param name="RSquared">Coefficient of determination (R²)</param>
    /// <param name="PointCount">Number of data points used</param>
    public readonly record struct RegressionResult(
        double Alpha,
        double Beta,
        double RSquared,
        int PointCount);

    /// <summary>
    /// Performs log₁₀-log₁₀ linear regression:
    ///   log₁₀(⟨ε_vac⟩) = α · log₁₀(N) + β
    ///
    /// Filters out data points where AvgVacuumEnergy ≤ 0 or N ≤ 0.
    /// </summary>
    public static RegressionResult ComputeLogLogRegression(IReadOnlyList<ScalingDataPoint> data)
    {
        ArgumentNullException.ThrowIfNull(data);

        // Collect valid log-log pairs
        List<(double logN, double logE)> pairs = [];

        foreach (ScalingDataPoint p in data)
        {
            if (p.N > 0 && p.AvgVacuumEnergy > 0)
            {
                pairs.Add((Math.Log10(p.N), Math.Log10(p.AvgVacuumEnergy)));
            }
        }

        if (pairs.Count < 2)
        {
            return new RegressionResult(Alpha: 0, Beta: 0, RSquared: 0, PointCount: pairs.Count);
        }

        // Simple linear regression: y = α·x + β
        double n = pairs.Count;
        double sumX = 0, sumY = 0, sumXX = 0, sumXY = 0, sumYY = 0;

        foreach ((double x, double y) in pairs)
        {
            sumX += x;
            sumY += y;
            sumXX += x * x;
            sumXY += x * y;
            sumYY += y * y;
        }

        double denom = n * sumXX - sumX * sumX;
        if (Math.Abs(denom) < 1e-15)
        {
            return new RegressionResult(Alpha: 0, Beta: sumY / n, RSquared: 0, PointCount: pairs.Count);
        }

        double alpha = (n * sumXY - sumX * sumY) / denom;
        double beta = (sumY - alpha * sumX) / n;

        // R² = 1 − SS_res / SS_tot
        double meanY = sumY / n;
        double ssTot = sumYY - n * meanY * meanY;
        double ssRes = 0;

        foreach ((double x, double y) in pairs)
        {
            double predicted = alpha * x + beta;
            double residual = y - predicted;
            ssRes += residual * residual;
        }

        double rSquared = ssTot > 1e-15 ? 1.0 - (ssRes / ssTot) : 0;

        return new RegressionResult(
            Alpha: alpha,
            Beta: beta,
            RSquared: Math.Clamp(rSquared, 0, 1),
            PointCount: pairs.Count);
    }

    /// <summary>
    /// Interprets the scaling exponent α as a scientific verdict.
    /// </summary>
    public static string InterpretAlpha(double alpha)
    {
        return alpha switch
        {
            > -0.1 => "FAILURE: No vacuum energy dilution (vacuum catastrophe persists, α ≈ 0)",
            > -0.35 => "WEAK: Marginal dilution detected (α ≈ −0.25), not significant",
            > -0.65 => "PARTIAL SUCCESS: Dilution ~ 1/√N (α ≈ −0.5), partial compensation",
            > -0.85 => "STRONG: Significant dilution (α ≈ −0.75), approaching full compensation",
            _ => "FULL SUCCESS: Dilution ~ 1/N (α ≈ −1), full vacuum energy compensation — publishable result"
        };
    }

    // ============================================================
    // Summary report
    // ============================================================

    /// <summary>
    /// Generates a human-readable summary report of the scaling experiment.
    /// </summary>
    public static string GenerateSummaryReport(IReadOnlyList<ScalingDataPoint> data)
    {
        ArgumentNullException.ThrowIfNull(data);

        var sb = new StringBuilder();
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine("  VACUUM ENERGY SCALING EXPERIMENT — RESULTS SUMMARY");
        sb.AppendLine("═══════════════════════════════════════════════════════════════");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Timestamp:    {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Data Points:  {data.Count}");
        sb.AppendLine();

        if (data.Count == 0)
        {
            sb.AppendLine("  No data points collected.");
            return sb.ToString();
        }

        // Data table
        sb.AppendLine("  ┌──────────┬───────────┬──────────────┬──────────────┬──────────┬──────────────┐");
        sb.AppendLine("  │     N    │ N_vacuum  │  ⟨ε_vac⟩     │   σ²(ε)      │   d_S    │  ⟨κ_OR⟩      │");
        sb.AppendLine("  ├──────────┼───────────┼──────────────┼──────────────┼──────────┼──────────────┤");

        foreach (ScalingDataPoint p in data)
        {
            sb.Append(CultureInfo.InvariantCulture, $"  │ {p.N,8} ");
            sb.Append(CultureInfo.InvariantCulture, $"│ {p.VacuumNodeCount,9} ");
            sb.Append(CultureInfo.InvariantCulture, $"│ {p.AvgVacuumEnergy,12:G5} ");
            sb.Append(CultureInfo.InvariantCulture, $"│ {p.EnergyVariance,12:G5} ");
            sb.Append(CultureInfo.InvariantCulture, $"│ {p.SpectralDimension,8:F3} ");
            sb.AppendLine(CultureInfo.InvariantCulture, $"│ {p.AvgRicciCurvature,12:G5} │");
        }

        sb.AppendLine("  └──────────┴───────────┴──────────────┴──────────────┴──────────┴──────────────┘");
        sb.AppendLine();

        // Log-log regression
        RegressionResult reg = ComputeLogLogRegression(data);

        sb.AppendLine("  LOG-LOG REGRESSION:  log₁₀(⟨ε_vac⟩) = α · log₁₀(N) + β");
        sb.AppendLine("  ───────────────────────────────────────────────────────────");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  α (slope)  = {reg.Alpha:F4}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  β (offset) = {reg.Beta:F4}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  R²         = {reg.RSquared:F4}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Points     = {reg.PointCount}");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"  VERDICT: {InterpretAlpha(reg.Alpha)}");
        sb.AppendLine();

        // Timing
        double totalSeconds = data.Sum(p => p.WallClockSeconds);
        sb.AppendLine(CultureInfo.InvariantCulture, $"  Total wall-clock time: {totalSeconds:F1} s ({totalSeconds / 60:F1} min)");
        sb.AppendLine();
        sb.AppendLine("═══════════════════════════════════════════════════════════════");

        return sb.ToString();
    }
}
