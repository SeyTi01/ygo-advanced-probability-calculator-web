namespace YGOProbabilityCalculatorBlazor.Services.Interface;

public interface IFileService {
    Task<string> ReadAllTextAsync(string path);
    Task<string[]> ReadAllLinesAsync(string path);
}
