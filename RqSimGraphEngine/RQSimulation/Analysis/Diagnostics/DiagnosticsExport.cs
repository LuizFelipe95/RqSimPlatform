using System;
using System.Collections.Generic;
using System.Linq;

namespace RQSimulation
{
    public static class DiagnosticsExport
    {
        public static void ExportLaplacianSpectrum(RQGraph graph, string path)
        {
            var spectrum = graph.ComputeLaplacianEigenvalues();
            System.IO.File.WriteAllLines(path, spectrum.Select(e => e.ToString("G17")));
        }

        public static void ExportHeavyClusterMasses(RQGraph graph, double minMass, string path)
        {
            var clusters = graph.GetHeavyClustersByMass(minMass);
            var lines = new List<string>();
            foreach (var cl in clusters)
            {
                double mass = 0.0;
                for (int a = 0; a < cl.Count; a++)
                {
                    int v = cl[a];
                    for (int b = a + 1; b < cl.Count; b++)
                    {
                        int u = cl[b];
                        if (!graph.Edges[v, u]) continue;
                        double w = graph.Weights[v, u];
                        if (w <= RQGraph.HeavyClusterThreshold) continue;
                        mass += (w - RQGraph.HeavyClusterThreshold);
                    }
                }
                lines.Add($"{cl.Count},{mass:G17}");
            }
            System.IO.File.WriteAllLines(path, lines);
        }
    }
}
