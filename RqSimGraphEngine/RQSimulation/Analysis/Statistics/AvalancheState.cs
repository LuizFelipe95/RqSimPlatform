namespace RQSimulation
{
    /// <summary>
    /// Тип лавины по изменению массы тяжёлого кластера
    /// </summary>
    public enum AvalancheKind
    {
        Synthesis,
        Decay,
        Neutral
    }

    /// <summary>
    /// 
    /// </summary>
    public class AvalancheState
    {
        public bool Active { get; set; }
        public int Index { get; set; }
        public int Start { get; set; }
        public int MaxOn { get; set; }
        public int TotalFlips { get; set; }
        public int Volume { get; set; }
        public int MaxCluster { get; set; }
        public double BaselineMean { get; set; }
        public double BaselineStd { get; set; }

        // RQ measurement tracking
        public double MaxCorr { get; set; }
        public double CorrAtEnd { get; set; }
        public bool MeasurementTriggered { get; set; }
        public bool TouchesSystem { get; set; }
        public bool TouchesApparatus { get; set; }
        public double CorrAtStart { get; set; }
        public int MaxSystemOn { get; set; }
        public int MaxApparatusOn { get; set; }

        // Heavy cluster tracking (íóêëåîñèíòåç)
        public int HeavyCountBefore { get; set; }
        public double HeavyMassBefore { get; set; }
        public int HeavyMaxSizeBefore { get; set; }
        public double HeavyBindingBefore { get; set; }

        public int HeavyCountAfter { get; set; }
        public double HeavyMassAfter { get; set; }
        public int HeavyMaxSizeAfter { get; set; }
        public double HeavyBindingAfter { get; set; }

        public double HeavyMassMaxDurante { get; set; }
        public int HeavySizeMaxDuring { get; set; }

        /// <summary>
        /// Изменение массы между окончанием предыдущей лавины и началом текущей
        /// </summary>
        public double DeltaHeavyMassBetween { get; set; }

        /// <summary>
        /// Полное изменение массы: внутри лавины + между лавинами
        /// </summary>
        public double DeltaHeavyMassTotal { get; set; }

        /// <summary>
        /// Флаг: масса изменилась внутри лавины (а не только на фоне)
        /// </summary>
        public bool HeavyMassChangedInside { get; set; }

        /// <summary>
        /// Тип лавины по изменению массы тяжёлого кластера
        /// </summary>
        public AvalancheKind Kind { get; set; } = AvalancheKind.Neutral;

        public void Reset()
        {
            Active = false;
            Volume = 0;
            MaxCluster = 0;
            MaxCorr = 0.0;
            CorrAtEnd = 0.0;
            MeasurementTriggered = false;
            TouchesSystem = false;
            TouchesApparatus = false;
            MaxSystemOn = 0;
            MaxApparatusOn = 0;

            HeavyCountBefore = 0;
            HeavyMassBefore = 0.0;
            HeavyMaxSizeBefore = 0;
            HeavyBindingBefore = 0.0;
            HeavyCountAfter = 0;
            HeavyMassAfter = 0.0;
            HeavyMaxSizeAfter = 0;
            HeavyBindingAfter = 0.0;

            HeavyMassMaxDurante = 0.0;
            HeavySizeMaxDuring = 0;

            DeltaHeavyMassBetween = 0.0;
            DeltaHeavyMassTotal = 0.0;
            HeavyMassChangedInside = false;

            // Сбрасываем тип лавины
            Kind = AvalancheKind.Neutral;
        }
    }
}
