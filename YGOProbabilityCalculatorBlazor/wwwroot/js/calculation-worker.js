// A separate runtime reuses the app's .NET 10 assets; it never starts Blazor's UI entry point.
try {
    const { dotnet } = await import('../_framework/dotnet.js');
    const runtime = await dotnet.create();
    const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
    const api = exports.YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation.CalculationWorkerExports;
    self.onmessage = ({ data }) => {
        try {
            if (data.fastStage) {
                let response;
                if (data.fastStage === 'initial') response = api.BeginFast(data.json);
                else if (data.fastStage === 'upgrade') response = api.UpgradeFast(data.json, data.remainingWork);
                else if (data.fastStage === 'progressive-exact-v1') response = api.UpgradeProgressiveFast(data.json);
                else throw new Error('Unknown calculation stage.');
                const stage = JSON.parse(response);
                self.postMessage({ id: data.id, response: stage.Response, remainingWork: stage.RemainingWork });
            } else {
                if (data.execution != null && data.execution !== 'interactive-exact-v1') throw new Error('Unknown work execution policy.');
                self.postMessage({ id: data.id, response: data.execution === 'interactive-exact-v1'
                    ? api.CalculateInteractiveExact(data.json) : api.Calculate(data.json) });
            }
        } catch (error) {
            self.postMessage({ id: data.id, error: error?.message || 'Background calculation failed.' });
        }
    };
    self.postMessage({ ready: true });
} catch (error) {
    self.postMessage({ error: `Background calculation could not start: ${error?.message || 'Runtime unavailable.'}` });
}
