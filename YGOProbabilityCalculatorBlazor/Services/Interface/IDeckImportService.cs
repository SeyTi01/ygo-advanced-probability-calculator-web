using Microsoft.AspNetCore.Components.Forms;
using YGOProbabilityCalculatorBlazor.Models;

namespace YGOProbabilityCalculatorBlazor.Services.Interface;

public interface IDeckImportService
{
    Task<List<Card>> ImportDeckFromYdkAsync(IBrowserFile file);

    Task<List<Card>> ImportDeckFromYdkeAsync(string ydke);
}
