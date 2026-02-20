using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RQSimulation
{
    public partial class RQGraph
    {
        // Diagnostic fields for spectral dimension analysis
        private double _lastSpectralSlope;
        private string _lastSpectralMethod = "none";


        // RQ-FIX: Spectral dimension smoothing to prevent method-switching jumps
        private const double SpectralDimSmoothingAlpha = 0.3; // EMA smoothing factor

        /// <summary>
        /// Last computed slope from eigenvalue or return probability analysis.
        /// Useful for debugging d_S computation.
        /// </summary>
        public double LastSpectralSlope => _lastSpectralSlope;

        /// <summary>
        /// Method used for last spectral dimension computation.
        /// Values: "HeatKernel", "RandomWalk", "Laplacian", "fallback"
        /// </summary>
        public string LastSpectralMethod => _lastSpectralMethod;

        /// <summary>
        /// Smoothed spectral dimension (EMA filtered to prevent jumps).
        /// </summary>
        public double SmoothedSpectralDimension => _smoothedSpectralDimension;

        /// <summary>
        /// Cache the spectral dimension with EMA smoothing.
        /// Called internally by ComputeSpectralDimension.
        /// NOTE: This method updates _smoothedSpectralDimension - the main implementation
        /// is also in RQGraph.SpectralGeometry.cs but works with _cachedSpectralDim.
        /// </summary>
        private void ApplySpectralDimensionSmoothing(double rawValue)
        {
            if (rawValue > 0 && !double.IsNaN(rawValue))
            {
                if (double.IsNaN(_smoothedSpectralDimension))
                {
                    // First valid value — seed the EMA directly
                    _smoothedSpectralDimension = rawValue;
                }
                else
                {
                    // Exponential moving average to smooth jumps between methods
                    _smoothedSpectralDimension = SpectralDimSmoothingAlpha * rawValue
                                               + (1.0 - SpectralDimSmoothingAlpha) * _smoothedSpectralDimension;
                }
            }
        }

        /// <summary>
        /// Вычисляет Спектральную Размерность графа.
        /// 
        /// RQ-гипотеза предсказывает:
        /// - d_S ≈ 2 на Планковских масштабах (UV)
        /// - d_S ≈ 4 на макроскопических масштабах (IR)
        /// - Переход между режимами при t ~ 1/m_Planck
        /// 
        /// Используем три метода с автоматическим выбором:
        /// 1. Heat Kernel trace: Tr(e^{-tL}) ~ t^{-d_S/2} — наиболее надёжный
        /// 2. Random Walk: P(t) ~ t^{-d_S/2} — физически мотивированный
        /// 3. Laplacian eigenvalues: λ_k ~ k^{2/d_S} — для очень плотных графов
        /// </summary>
        public double ComputeSpectralDimension(int t_max = 100, int num_walkers = 500)
        {
            if (N < 10)
            {
                _lastSpectralMethod = "fallback";
                _lastSpectralSlope = 0;
                return 2.0;
            }

            // Вычисляем характеристики графа
            int edgeCount = 0;
            for (int i = 0; i < N; i++)
                edgeCount += _degree[i];
            edgeCount /= 2;

            int maxEdges = N * (N - 1) / 2;
            double density = maxEdges > 0 ? (double)edgeCount / maxEdges : 0;
            double avgDegree = N > 0 ? 2.0 * edgeCount / N : 0;

            // === RQ-FIX: Выбор метода на основе физических соображений ===

            // Для очень плотных графов (density > 20%) — Laplacian работает лучше
            if (density > 0.20)
            {
                return ComputeSpectralDimensionLaplacian();
            }

            // Для графов с низкой связностью — Random Walk
            // Для умеренных графов — Heat Kernel (более стабильный)
            double result;
            if (avgDegree < 4.0)
            {
                // Разреженный граф — Random Walk
                result = ComputeSpectralDimensionRandomWalk(t_max, num_walkers);
            }
            else
            {
                // Умеренно связанный — Heat Kernel как основной
                double d_hk = ComputeSpectralDimensionHeatKernel(t_max);

                // Если Heat Kernel даёт разумный результат, используем его
                if (d_hk > 1.0 && d_hk < 8.0)
                {
                    result = d_hk;
                }
                else
                {
                    // Иначе пробуем Random Walk
                    double d_rw = ComputeSpectralDimensionRandomWalk(t_max, num_walkers);
                    if (d_rw > 1.0 && d_rw < 8.0)
                    {
                        result = d_rw;
                    }
                    else
                    {
                        // Fallback к Laplacian
                        result = ComputeSpectralDimensionLaplacian();
                    }
                }
            }

            // Кэшируем результат и применяем сглаживание
            ApplySpectralDimensionSmoothing(result);
            return result;
        }

        /// <summary>
        /// Heat Kernel метод для спектральной размерности.
        /// 
        /// Tr(e^{-tL}) ~ t^{-d_S/2} для малых t
        /// 
        /// Это наиболее физически мотивированный метод:
        /// Heat kernel — это пропагатор диффузии на графе,
        /// его асимптотика определяет эффективную размерность.
        /// 
        /// Алгоритм: вычисляем trace через стохастическую оценку
        /// с использованием random vectors (Hutchinson's trick).
        /// </summary>
        private double ComputeSpectralDimensionHeatKernel(int t_max)
        {
            _lastSpectralMethod = "HeatKernel";

            // Строим нормализованный Лапласиан
            var degree = new double[N];
            for (int i = 0; i < N; i++)
            {
                double deg = 0;
                foreach (int j in Neighbors(i))
                    deg += Weights[i, j];
                degree[i] = deg;
            }

            // Используем стохастическую оценку trace через random vectors
            int numVectors = Math.Min(50, N); // Hutchinson estimator
            var traces = new double[t_max + 1];

            var rng = new Random(42); // Детерминистический seed для воспроизводимости

            for (int v = 0; v < numVectors; v++)
            {
                // Random ±1 vector (Rademacher)
                var z = new double[N];
                for (int i = 0; i < N; i++)
                    z[i] = rng.NextDouble() < 0.5 ? -1.0 : 1.0;

                // Эволюция: y(t+1) = (I - dt*L_norm) y(t)
                // где L_norm = D^{-1/2} L D^{-1/2}
                var y = (double[])z.Clone();
                double dt = 0.1; // Шаг времени для diffusion

                for (int t = 1; t <= t_max; t++)
                {
                    var yNew = new double[N];

                    for (int i = 0; i < N; i++)
                    {
                        double sum = y[i]; // Diagonal term (1 - dt*1) = 1 - dt

                        if (degree[i] > 1e-10)
                        {
                            double dInvSqrt_i = 1.0 / Math.Sqrt(degree[i]);

                            foreach (int j in Neighbors(i))
                            {
                                if (degree[j] > 1e-10)
                                {
                                    double dInvSqrt_j = 1.0 / Math.Sqrt(degree[j]);
                                    double L_ij = -Weights[i, j] * dInvSqrt_i * dInvSqrt_j;
                                    sum -= dt * L_ij * y[j];
                                }
                            }

                            // Diagonal: L_ii = 1 (for normalized Laplacian)
                            sum -= dt * y[i];
                        }

                        yNew[i] = sum;
                    }

                    y = yNew;

                    // Trace estimate: z^T e^{-tL} z ≈ Tr(e^{-tL}) для random z
                    double traceEst = 0;
                    for (int i = 0; i < N; i++)
                        traceEst += z[i] * y[i];

                    traces[t] += traceEst / numVectors;
                }
            }

            // Fit: log(Tr) = -d_S/2 * log(t) + const
            // Используем диапазон t где trace ещё не затухло
            var logT = new List<double>();
            var logTrace = new List<double>();

            for (int t = 5; t <= Math.Min(t_max, 50); t++)
            {
                if (traces[t] > 1e-10)
                {
                    logT.Add(Math.Log(t * 0.1)); // Effective time = t * dt
                    logTrace.Add(Math.Log(traces[t]));
                }
            }

            if (logT.Count < 5)
            {
                _lastSpectralSlope = 0;
                return 2.0; // Недостаточно данных
            }

            // Linear regression
            double meanLogT = logT.Average();
            double meanLogTrace = logTrace.Average();

            double num = 0, den = 0;
            for (int i = 0; i < logT.Count; i++)
            {
                double dT = logT[i] - meanLogT;
                double dTr = logTrace[i] - meanLogTrace;
                num += dT * dTr;
                den += dT * dT;
            }

            if (Math.Abs(den) < 1e-10)
            {
                _lastSpectralSlope = 0;
                return 2.0;
            }

            double slope = num / den;
            _lastSpectralSlope = slope;

            // Tr(e^{-tL}) ~ t^{-d_S/2} => slope = -d_S/2 => d_S = -2*slope
            double d_S = -2.0 * slope;

            return Math.Clamp(d_S, 1.0, 10.0);
        }

        /// <summary>
        /// Laplacian-based spectral dimension calculation.
        /// Uses eigenvalue scaling: λ_k ~ k^(2/d_S) for small k.
        /// 
        /// Для RQ-гипотезы: этот метод лучше работает для плотных графов
        /// где random walk быстро термализуется.
        /// 
        /// Улучшения:
        /// - Больше eigenvalues (до 30)
        /// - Взвешенная регрессия (меньший вес для больших k)
        /// - Outlier rejection
        /// </summary>
        public double ComputeSpectralDimensionLaplacian()
        {
            _lastSpectralMethod = "Laplacian";

            if (N < 20)
            {
                _lastSpectralSlope = 0;
                return 2.0;
            }

            try
            {
                // Compute Laplacian spectrum (k smallest eigenvalues)
                int k = Math.Min(30, N / 3);
                var (eigenvalues, _) = ComputeLaplacianSpectrum(k);

                if (eigenvalues == null || eigenvalues.Length < 5)
                {
                    _lastSpectralSlope = 0;
                    return 2.0;
                }

                // Для нормализованного Лапласиана eigenvalues ∈ [0, 2]
                // λ_0 = 0 (тривиальный), λ_1 > 0 (spectral gap)

                var logK = new List<double>();
                var logLambda = new List<double>();
                var weights = new List<double>();

                // Используем eigenvalues начиная с λ_1
                // Вес обратно пропорционален k (первые eigenvalues важнее)
                for (int i = 1; i < eigenvalues.Length && i <= 25; i++)
                {
                    double lambda = eigenvalues[i];

                    // Фильтруем некорректные eigenvalues
                    if (lambda > 1e-6 && lambda < 1.99)
                    {
                        logK.Add(Math.Log(i));
                        logLambda.Add(Math.Log(lambda));
                        weights.Add(1.0 / i); // Weighted regression
                    }
                }

                if (logK.Count < 4)
                {
                    _lastSpectralSlope = 0;
                    return 2.0;
                }

                // Weighted linear regression
                double sumW = weights.Sum();
                double meanLogK = 0, meanLogLambda = 0;
                for (int i = 0; i < logK.Count; i++)
                {
                    meanLogK += weights[i] * logK[i];
                    meanLogLambda += weights[i] * logLambda[i];
                }
                meanLogK /= sumW;
                meanLogLambda /= sumW;

                double num = 0, den = 0;
                for (int i = 0; i < logK.Count; i++)
                {
                    double dK = logK[i] - meanLogK;
                    double dL = logLambda[i] - meanLogLambda;
                    num += weights[i] * dK * dL;
                    den += weights[i] * dK * dK;
                }

                if (Math.Abs(den) < 1e-10)
                {
                    _lastSpectralSlope = 0;
                    return 2.0;
                }

                double slope = num / den;
                _lastSpectralSlope = slope;

                // λ_k ~ k^(2/d_S) => log(λ) = (2/d_S) * log(k)
                // slope = 2/d_S => d_S = 2/slope

                double d_S;
                if (slope > 0.1)
                {
                    d_S = 2.0 / slope;
                }
                else if (slope > 0.01)
                {
                    // Очень малый slope — высокоразмерная геометрия
                    d_S = 2.0 / slope;
                }
                else
                {
                    // Degenerate case — все eigenvalues примерно равны
                    // Это означает expander graph / mean-field topology
                    d_S = 4.0; // Default to 4D для RQ
                }

                return Math.Clamp(d_S, 1.0, 10.0);
            }
            catch
            {
                _lastSpectralSlope = 0;
                _lastSpectralMethod = "fallback";
                return 2.0;
            }
        }

        /// <summary>
        /// Random Walk Return Probability метод.
        /// P(t) ~ t^(-d_S/2) => d_S = -2 * d(ln P)/d(ln t)
        /// 
        /// Физика: random walker на d-мерном пространстве возвращается
        /// в начальную точку с вероятностью P(t) ~ t^{-d/2}.
        /// 
        /// Улучшения для RQ:
        /// - Weighted random walk (учитывает Weights[i,j])
        /// - Больше walkers для статистики
        /// - Правильный диапазон t (не слишком малый, не слишком большой)
        /// - Robust regression
        /// 
        /// BUG FIX: Изолированные узлы (degree=0) не должны считаться как returns,
        /// так как walker фактически не двигался. Это согласовано с GPU реализацией.
        /// </summary>
        private double ComputeSpectralDimensionRandomWalk(int t_max, int num_walkers)
        {
            _lastSpectralMethod = "RandomWalk";

            // Масштабируем количество walkers с размером графа
            int effectiveWalkers = Math.Max(num_walkers, N * 20);

            // Ограничиваем t_max — слишком большое t даёт шум
            int effectiveTmax = Math.Min(t_max, 100);

            var returnCounts = new int[effectiveTmax + 1];
            object lockObj = new();

            // Параллельный запуск walkers
            Parallel.For(0, effectiveWalkers, () => new Random(Guid.NewGuid().GetHashCode()),
                (w, state, localRng) =>
                {
                    int startNode = localRng.Next(N);
                    int currentNode = startNode;
                    var localCounts = new int[effectiveTmax + 1];

                    for (int t = 1; t <= effectiveTmax; t++)
                    {
                        // BUG FIX: Track whether walker actually moved
                        int prevNode = currentNode;
                        currentNode = RandomWalkStep(currentNode, localRng);

                        // Only count return if walker actually moved this step
                        // Isolated nodes (staying in place) should NOT count as returns
                        bool didMove = (currentNode != prevNode);
                        if (didMove && currentNode == startNode)
                            localCounts[t]++;
                    }

                    lock (lockObj)
                    {
                        for (int t = 1; t <= effectiveTmax; t++)
                            returnCounts[t] += localCounts[t];
                    }

                    return localRng;
                },
                _ => { });

            // Вычисляем вероятности
            var returnProb = new double[effectiveTmax + 1];
            for (int t = 1; t <= effectiveTmax; t++)
                returnProb[t] = (double)returnCounts[t] / effectiveWalkers;

            // Robust linear regression с отбрасыванием нулей и выбросов
            // Используем диапазон t ∈ [10, t_max/2] — средние времена
            var logT = new List<double>();
            var logP = new List<double>();

            int t_start = Math.Max(5, effectiveTmax / 10);
            int t_end = effectiveTmax * 2 / 3;

            for (int t = t_start; t <= t_end; t++)
            {
                if (returnProb[t] > 1e-8)
                {
                    logT.Add(Math.Log(t));
                    logP.Add(Math.Log(returnProb[t]));
                }
            }

            if (logT.Count < 5)
            {
                _lastSpectralSlope = 0;
                // Мало возвратов — граф слишком большой или фрагментированный
                // Пробуем Laplacian метод
                return ComputeSpectralDimensionLaplacian();
            }

            // Linear regression
            double meanLogT = logT.Average();
            double meanLogP = logP.Average();

            double num = 0, den = 0;
            for (int i = 0; i < logT.Count; i++)
            {
                double dT = logT[i] - meanLogT;
                double dP = logP[i] - meanLogP;
                num += dT * dP;
                den += dT * dT;
            }

            if (Math.Abs(den) < 1e-10)
            {
                _lastSpectralSlope = 0;
                return 2.0;
            }

            double slope = num / den;
            _lastSpectralSlope = slope;

            // P(t) ~ t^{-d_S/2} => log(P) = (-d_S/2) * log(t)
            // slope = -d_S/2 => d_S = -2 * slope

            // Для правильной физики slope должен быть отрицательным
            // (вероятность возврата убывает со временем)
            if (slope >= 0)
            {
                // Walker застревает (компактный граф или ловушки)
                // Fallback к Laplacian
                return ComputeSpectralDimensionLaplacian();
            }

            double d_S = -2.0 * slope;

            return Math.Clamp(d_S, 1.0, 10.0);
        }

        private int RandomWalkStep(int node, Random rng)
        {
            double totalWeight = 0;
            foreach (int nb in Neighbors(node)) totalWeight += Weights[node, nb];
            if (totalWeight <= 0) return node;
            double r = rng.NextDouble() * totalWeight;
            double sum = 0;
            foreach (int nb in Neighbors(node))
            {
                sum += Weights[node, nb];
                if (r <= sum) return nb;
            }
            return node;
        }
    }
}
