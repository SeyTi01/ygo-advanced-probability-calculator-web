using Microsoft.AspNetCore.Components.Forms;

namespace YGOProbabilityCalculatorBlazor.Services.Interface;

public interface IFileService {
    Task<string[]> ReadAllLinesAsync(IBrowserFile file);
    Task<string> ReadAllTextAsync(string path);
}
