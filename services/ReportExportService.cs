using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using monitorProcess.controllers;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace monitorProcess.Services
{
    /// <summary>
    /// Xuất báo cáo thống kê tiến trình ra CSV/PDF, tách riêng 2 phần: tiến
    /// trình hợp lệ (không có cảnh báo) và tiến trình vi phạm (có cảnh báo).
    /// Tương ứng chức năng 7: Xuất báo cáo (Export Report CSV/PDF).
    /// </summary>
    public class ReportExportService
    {
        static ReportExportService()
        {
            // QuestPDF yêu cầu khai báo loại giấy phép trước khi dùng.
            // Community: miễn phí cho cá nhân/học tập/tổ chức nhỏ (doanh thu < 1 triệu USD/năm).
            QuestPDF.Settings.License = LicenseType.Community;
        }

        // ---------- CSV ----------

        /// <summary>Xuất 2 file CSV riêng biệt: hợp lệ và vi phạm. Trả về đường dẫn 2 file đã tạo.</summary>
        public (string validPath, string violationPath) ExportCsv(
            IEnumerable<ProcessRowViewModel> rows, string outputDirectory)
        {
            Directory.CreateDirectory(outputDirectory);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var validPath = Path.Combine(outputDirectory, $"tien_trinh_hop_le_{timestamp}.csv");
            var violationPath = Path.Combine(outputDirectory, $"tien_trinh_vi_pham_{timestamp}.csv");

            var validRows = rows.Where(r => !r.IsSuspicious).ToList();
            var violationRows = rows.Where(r => r.IsSuspicious).ToList();

            WriteCsv(validPath, validRows, includeAlert: false);
            WriteCsv(violationPath, violationRows, includeAlert: true);

            return (validPath, violationPath);
        }

        private void WriteCsv(string path, IEnumerable<ProcessRowViewModel> rows, bool includeAlert)
        {
            var sb = new StringBuilder();

            var headers = new List<string>
            {
                "PID", "Ten tien trinh", "User", "Trang thai", "Duong dan thuc thi",
                "CPU (%)", "RAM (MB)", "Doc (B/s)", "Ghi (B/s)", "Cong mang"
            };
            if (includeAlert) headers.Add("Canh bao");

            sb.AppendLine(string.Join(",", headers.Select(EscapeCsv)));

            foreach (var r in rows)
            {
                var fields = new List<string>
                {
                    r.Pid.ToString(),
                    r.Name,
                    r.UserName,
                    r.State,
                    r.ExePath,
                    r.CpuPercent.ToString("0.0", CultureInfo.InvariantCulture),
                    r.MemoryMb.ToString("0.0", CultureInfo.InvariantCulture),
                    r.ReadSpeedBps.ToString("0", CultureInfo.InvariantCulture),
                    r.WriteSpeedBps.ToString("0", CultureInfo.InvariantCulture),
                    r.Ports
                };
                if (includeAlert) fields.Add(r.AlertText);

                sb.AppendLine(string.Join(",", fields.Select(EscapeCsv)));
            }

            // Ghi kèm BOM (Byte Order Mark) để Excel nhận đúng UTF-8, tránh lỗi
            // hiển thị tiếng Việt bị vỡ font khi mở file trực tiếp bằng Excel.
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }

        private string EscapeCsv(string? field)
        {
            field ??= string.Empty;
            if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
            {
                field = field.Replace("\"", "\"\"");
                return $"\"{field}\"";
            }
            return field;
        }

        // ---------- PDF ----------

        /// <summary>Xuất 1 file PDF gồm 2 trang: trang 1 tiến trình hợp lệ, trang 2 tiến trình vi phạm.</summary>
        public string ExportPdf(IEnumerable<ProcessRowViewModel> rows, string outputFilePath)
        {
            var validRows = rows.Where(r => !r.IsSuspicious).ToList();
            var violationRows = rows.Where(r => r.IsSuspicious).ToList();

            var directory = Path.GetDirectoryName(outputFilePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            Document.Create(document =>
            {
                document.Page(page => BuildPage(page, "BAO CAO TIEN TRINH HOP LE", validRows, includeAlert: false));
                document.Page(page => BuildPage(page, "BAO CAO TIEN TRINH VI PHAM", violationRows, includeAlert: true));
            })
            .GeneratePdf(outputFilePath);

            return outputFilePath;
        }

        private void BuildPage(
            PageDescriptor page, string title, List<ProcessRowViewModel> rows, bool includeAlert)
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(24);
            page.DefaultTextStyle(x => x.FontSize(9));

            page.Header().Column(col =>
            {
                col.Item().Text(title).FontSize(16).Bold();
                col.Item().Text($"Thoi gian xuat: {DateTime.Now:dd/MM/yyyy HH:mm:ss}    |    So luong: {rows.Count}")
                    .FontSize(9).FontColor(Colors.Grey.Darken1);
                col.Item().PaddingTop(8);
            });

            page.Content().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(45);   // PID
                    columns.RelativeColumn(2);    // Tên
                    columns.RelativeColumn(1.2f);  // User
                    columns.RelativeColumn(1.2f);  // Trạng thái
                    columns.RelativeColumn(3);    // Đường dẫn
                    columns.ConstantColumn(45);   // CPU
                    columns.ConstantColumn(55);   // RAM
                    if (includeAlert) columns.RelativeColumn(3); // Cảnh báo
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell).Text("PID");
                    header.Cell().Element(HeaderCell).Text("Ten tien trinh");
                    header.Cell().Element(HeaderCell).Text("User");
                    header.Cell().Element(HeaderCell).Text("Trang thai");
                    header.Cell().Element(HeaderCell).Text("Duong dan thuc thi");
                    header.Cell().Element(HeaderCell).Text("CPU%");
                    header.Cell().Element(HeaderCell).Text("RAM(MB)");
                    if (includeAlert) header.Cell().Element(HeaderCell).Text("Canh bao");
                });

                foreach (var r in rows)
                {
                    table.Cell().Element(BodyCell).Text(r.Pid.ToString());
                    table.Cell().Element(BodyCell).Text(r.Name);
                    table.Cell().Element(BodyCell).Text(r.UserName);
                    table.Cell().Element(BodyCell).Text(r.State);
                    table.Cell().Element(BodyCell).Text(r.ExePath);
                    table.Cell().Element(BodyCell).Text(r.CpuPercent.ToString("0.0"));
                    table.Cell().Element(BodyCell).Text(r.MemoryMb.ToString("0.0"));

                    if (includeAlert)
                        table.Cell().Element(BodyCell).Text(r.AlertText).FontColor(Colors.Red.Darken2);
                }

                static IContainer HeaderCell(IContainer c) =>
                    c.Background(Colors.Grey.Lighten2).Padding(4).DefaultTextStyle(x => x.Bold());

                static IContainer BodyCell(IContainer c) =>
                    c.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4);
            });

            page.Footer().AlignCenter().Text(x =>
            {
                x.CurrentPageNumber();
                x.Span(" / ");
                x.TotalPages();
            });
        }
    }
}