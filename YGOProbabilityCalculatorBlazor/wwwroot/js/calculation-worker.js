// A separate runtime reuses the app's .NET 10 assets; it never starts Blazor's UI entry point.
try {
    const { dotnet } = await import('../_framework/dotnet.js');
    const runtime = await dotnet.create();
    const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
    const api = exports.YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation.CalculationWorkerExports;
    self.onmessage = ({ data }) => {
        try {
            if (data.fastStage) {
                const stage = JSON.parse(data.fastStage === 'initial' ? api.BeginFast(data.json)
                    : api.UpgradeFast(data.json, data.remainingWork));
                self.postMessage({ id: data.id, response: stage.Response, remainingWork: stage.RemainingWork });
            } else {
                self.postMessage({ id: data.id, response: api.Calculate(data.json) });
            }
        } catch (error) {
            self.postMessage({ id: data.id, error: error?.message || 'Background calculation failed.' });
        }
    };
    self.postMessage({ ready: true });
} catch (error) {
    self.postMessage({ error: `Background calculation could not start: ${error?.message || 'Runtime unavailable.'}` });
}
