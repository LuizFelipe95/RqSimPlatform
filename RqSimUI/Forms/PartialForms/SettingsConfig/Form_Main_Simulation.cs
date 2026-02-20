using RqSimUI.FormSimAPI.Interfaces;
using RQSimulation;
using System.Diagnostics;

namespace RqSimForms;

public partial class Form_Main_RqSim
{
    private bool _isModernRunning { get => _simApi.IsModernRunning; set => _simApi.IsModernRunning = value; }

    /// <summary>
    /// Reads simulation configuration from UI controls with clamped ranges.
    /// </summary>
    private SimulationConfig GetConfigFromUI()
    {
        var config = new SimulationConfig
        {
            NodeCount = (int)numNodeCount.Value,
            InitialEdgeProb = (double)numInitialEdgeProb.Value,
            InitialExcitedProb = (double)numInitialExcitedProb.Value,
            TargetDegree = (int)numTargetDegree.Value,
            LambdaState = (double)numLambdaState.Value,
            Temperature = (double)numTemperature.Value,
            EdgeTrialProbability = (double)numEdgeTrialProb.Value,
            MeasurementThreshold = (double)numMeasurementThreshold.Value,
            Seed = 42,
            TotalSteps = Math.Max(1, (int)numTotalSteps.Value),
            LogEvery = 1,
            BaselineWindow = 50,
            FirstImpulse = -1,
            ImpulsePeriod = -1,
            CalibrationStep = -1,
            VisualizationInterval = 1,
            MeasurementLogInterval = 50,
            FractalLevels = (int)numFractalLevels.Value,
            FractalBranchFactor = (int)numFractalBranchFactor.Value,

            UseQuantumDrivenStates = chkQuantumDriven.Checked,
            UseSpacetimePhysics = chkSpacetimePhysics.Checked,
            UseSpinorField = chkSpinorField.Checked,
            UseVacuumFluctuations = chkVacuumFluctuations.Checked,
            UseBlackHolePhysics = chkBlackHolePhysics.Checked,
            UseYangMillsGauge = chkYangMillsGauge.Checked,
            UseEnhancedKleinGordon = chkEnhancedKleinGordon.Checked,
            UseInternalTime = chkInternalTime.Checked,
            UseSpectralGeometry = chkSpectralGeometry.Checked,
            UseQuantumGraphity = chkQuantumGraphity.Checked,

            GravitationalCoupling = (double)numGravitationalCoupling.Value,
            VacuumEnergyScale = (double)numVacuumEnergyScale.Value,
            DecoherenceRate = (double)numDecoherenceRate.Value,
            HotStartTemperature = (double)numHotStartTemperature.Value,
            InitialNetworkTemperature = (double)numHotStartTemperature.Value,

            UseRelationalTime = chkRelationalTime.Checked,
            UseRelationalYangMills = chkRelationalYangMills.Checked,
            UseNetworkGravity = chkNetworkGravity.Checked,
            UseUnifiedPhysicsStep = chkUnifiedPhysicsStep.Checked,
            EnforceGaugeConstraints = chkEnforceGaugeConstraints.Checked,
            UseCausalRewiring = chkCausalRewiring.Checked,
            UseTopologicalProtection = chkTopologicalProtection.Checked,
            ValidateEnergyConservation = chkValidateEnergyConservation.Checked,
            UseMexicanHatPotential = chkMexicanHatPotential.Checked,
            UseHotStartAnnealing = false,
            UseGeometryMomenta = chkGeometryMomenta.Checked,
            UseTopologicalCensorship = chkTopologicalCensorship.Checked,

            UseEventBasedSimulation = true
        };

        if (config.InitialExcitedProb < 0) config.InitialExcitedProb = 0;
        if (config.InitialExcitedProb > 1) config.InitialExcitedProb = 1;
        if (config.EdgeTrialProbability < 0) config.EdgeTrialProbability = 0;
        if (config.EdgeTrialProbability > 1) config.EdgeTrialProbability = 1;
        if (config.MeasurementThreshold < 0) config.MeasurementThreshold = 0;
        if (config.VisualizationInterval < 1) config.VisualizationInterval = 1;
        if (config.MeasurementLogInterval < 1) config.MeasurementLogInterval = 1;
        if (config.InitialEdgeProb < 0) config.InitialEdgeProb = 0;
        if (config.InitialEdgeProb > 1) config.InitialEdgeProb = 1;
        if (config.GravitationalCoupling < 0) config.GravitationalCoupling = 0;
        if (config.DecoherenceRate < 0) config.DecoherenceRate = 0;

        return config;
    }
}
