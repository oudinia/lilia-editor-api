using Lilia.Core.Entities;
using Lilia.Core.Models.Epub;

namespace Lilia.Core.Interfaces;

public interface IEpubService
{
    Task<EpubAnalysisResult> AnalyzeAsync(Stream epubStream);
    Task<(EpubMetadata Metadata, List<Block> Blocks, List<string> Warnings)> ImportAsync(Stream epubStream);
    Task<byte[]> ExportAsync(List<Block> blocks, EpubExportOptions options);
    Task<byte[]> CleanAndRepackageAsync(Stream epubStream, EpubExportOptions? options = null);
}
