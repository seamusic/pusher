using Markdig;
using MessagePusher.Application.Abstractions;

namespace MessagePusher.Infrastructure.Markdown;

public sealed class MarkdigRenderer : IMarkdownRenderer
{
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    public string ToHtml(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return "";
        try
        {
            return Markdig.Markdown.ToHtml(markdown, _pipeline);
        }
        catch (Exception ex)
        {
            return $"Markdown 渲染出错：{ex.Message}";
        }
    }
}
