using System.Text;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;

namespace Portion.Server.Utilities
{
    /// <summary>
    /// Static utility for extracting plain text from PDF, DOCX, and TXT resume files.
    /// </summary>
    public static class TextExtractor
    {
        public static string ExtractFromFile(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();

            return ext switch
            {
                ".pdf" => ExtractFromPdf(filePath),
                ".docx" => ExtractFromDocx(filePath),
                _ => File.ReadAllText(filePath)
            };
        }

        private static string ExtractFromPdf(string filePath)
        {
            using var document = PdfDocument.Open(filePath);
            var builder = new StringBuilder();
            foreach (var page in document.GetPages())
                builder.AppendLine(page.Text);
            return builder.ToString();
        }

        private static string ExtractFromDocx(string filePath)
        {
            using var doc = WordprocessingDocument.Open(filePath, isEditable: false);
            return doc.MainDocumentPart?.Document.Body?.InnerText ?? string.Empty;
        }
    }
}
