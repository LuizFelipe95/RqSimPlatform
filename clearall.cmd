rd /q /s .vs
rd /q /s TestResults

cd Dx12WinForm
rd /s /q bin
rd /q /s obj
cd ..

cd RqSim.PluginManager.UI
rd /s /q bin
rd /q /s obj
cd ..

cd RqSim3DForm
rd /s /q bin
rd /q /s obj
cd ..

cd RqSimConsole
rd /s /q bin
rd /q /s obj
cd ..

cd RqSimEngineApi
rd /s /q bin
rd /q /s obj
cd ..

cd RqSimGraphEngine
rd /s /q bin
rd /q /s obj
cd ..

cd RqSimRenderingEngine
rd /s /q bin
rd /q /s obj
cd ..

cd RqSimRenderingEngine.Abstractions
rd /s /q bin
rd /q /s obj
cd ..

cd RqSimTelemetryForm
rd /s /q bin
rd /q /s obj
cd ..

cd RqSimPlatform.Contracts
rd /s /q bin
rd /q /s obj
cd ..


cd RqSimUI
rd /s /q bin
rd /q /s obj
cd ..

cd RqSimVisualization
rd /s /q bin
rd /q /s obj
cd ..

cd Tests\RqSimConsoleTest
rd /s /q bin
rd /q /s obj
cd ..\..

cd Tests\RqsimExperimetsTests
rd /s /q bin
rd /q /s obj
cd ..\..

cd Tests\RqSimGPUCPUTests
rd /s /q bin
rd /q /s obj
cd ..\..

cd Tests\RqSimRenderingEngine.Dx12Tests
rd /s /q bin
rd /q /s obj
cd ..\..

cd Tests\RqSimRenderingEngine.Tests
rd /s /q bin
rd /q /s obj
cd ..\..

echo Clean complete.



