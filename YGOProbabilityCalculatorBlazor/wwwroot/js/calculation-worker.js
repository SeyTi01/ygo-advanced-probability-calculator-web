// A separate runtime reuses the app's .NET 10 assets; it never starts Blazor's UI entry point.
try {
    const { dotnet } = await import('../_framework/dotnet.js');
    const runtime = await dotnet.create();
    const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
    const calculate = exports.YGOProbabilityCalculatorBlazor.Services.BackgroundCalculation.CalculationWorkerExports.Calculate;
    self.onmessage = ({ data }) => {
        try {
            self.postMessage({ id: data.id, response: calculate(data.json) });
        } catch (error) {
            self.postMessage({ id: data.id, error: error?.message || 'Background calculation failed.' });
        }
    };
    self.postMessage({ ready: true });
} catch (error) {
    self.postMessage({ error: `Background calculation could not start: ${error?.message || 'Runtime unavailable.'}` });
}
