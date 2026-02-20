
using System;

namespace RqSimGraphEngine.RQSimulation.Core.Constants
{
    /// <summary>
    /// Расширение констант для феноменологической настройки симуляции.
    /// Содержит параметры, подогнанные для численной стабильности (Numerical Stability) 
    /// и предотвращения топологических сингулярностей.
    /// </summary>
    public static partial class PhysicsConstants
    {
        public static class Fitted
        {
            /// <summary>
            /// <para><strong>Значение в коде:</strong> 1.0e-4</para>
            /// <para><strong>Реальность:</strong> ~1e-122</para>
            /// <para><strong>Обоснование:</strong> Критическая подгонка. Реальное значение слишком мало 
            /// для наблюдения динамики расширения за разумное время симуляции.</para>
            /// </summary>
            public const double CosmologicalConstant = 1.0e-4d;

            /// <summary>
            /// <para><strong>Значение в коде:</strong> 0.1</para>
            /// <para><strong>Реальность:</strong> 1.0 (Planck units)</para>
            /// <para><strong>Обоснование:</strong> Ослаблена в 10 раз. При G=1 на малых графах (N &lt; 1000) 
            /// возникают мгновенные сингулярности (схлопывание узлов).</para>
            /// </summary>
            public const double GravitationalCoupling = 0.1d;

            /// <summary>
            /// <para><strong>Значение в коде:</strong> 1.0 / 137.0</para>
            /// <para><strong>Реальность:</strong> ~1 / 137.036</para>
            /// <para><strong>Обоснование:</strong> Используется double для точности в QED вычислениях. 
            /// Округление допустимо для структуры графа, но важно для амплитуд.</para>
            /// </summary>
            public const double FineStructureConstant = 1.0d / 137.0d;

            /// <summary>
            /// <para><strong>Значение в коде:</strong> 1000.0</para>
            /// <para><strong>Реальность:</strong> ~1e-27 kg/m^3</para>
            /// <para><strong>Обоснование:</strong> "Энергетический бюджет" для инициализации Metropolis-алгоритма. 
            /// Определяет начальную активность перестройки связей.</para>
            /// </summary>
            public const double VacuumEnergyDensity = 1000.0d;

            /// <summary>
            /// <para><strong>Значение в коде:</strong> 1.0</para>
            /// <para><strong>Реальность:</strong> 1.6e-35 m</para>
            /// <para><strong>Обоснование:</strong> Единица длины ребра в Relational подходе. 
            /// Делает граф дискретным ("зернистым"), упрощая вычисления метрики.</para>
            /// </summary>
            public const double PlanckLength = 1.0d;

            /// <summary>
            /// <para><strong>Значение в коде:</strong> 0.5</para>
            /// <para><strong>Реальность:</strong> 1.0 (c)</para>
            /// <para><strong>Обоснование:</strong> Искусственное замедление (0.5c) для синхронизации 
            /// распространения сигналов с тактами обновления CPU/GPU (Tick Rate).</para>
            /// </summary>
            public const double InformationFlowRate = 0.5d;

            /// <summary>
            /// <para><strong>Значение в коде:</strong> 10.0</para>
            /// <para><strong>Реальность:</strong> 0 (у геометрии нет массы)</para>
            /// <para><strong>Обоснование:</strong> Фиктивная инерция узлов. Работает как Damping (демпфирование), 
            /// чтобы метрика не флуктуировала хаотично каждый кадр.</para>
            /// </summary>
            public const double GeometryInertia = 10.0d;
        }
    }
}
