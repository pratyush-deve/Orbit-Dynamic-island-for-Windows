using System.Windows.Media;

namespace Orbit.Services.DevWorkspace;

public interface IIconExtractorService
{
    ImageSource? GetOrExtractIcon(string? filePath);
}

