using System;
using System.Collections.Generic;

namespace MinhTamHair.Services;
public interface IEInvoiceService
{
    Task<EInvoiceResult> PhatHanhHoaDonAsync(string maHddt);
}

public class EInvoiceResult
{
    public bool Success { get; set; }
    public string? SoHoaDon { get; set; }
    public string? LinkPdf { get; set; }
    public string? LinkXml { get; set; }
    public string? ReferenceCode { get; set; }
    public string? ErrorMessage { get; set; }
}

// Dùng nội bộ để build payload gửi lên SePay, đặt tên đúng theo API
public class EInvoiceBuyerDto
{
    public string Type { get; set; } = "personal"; // "personal" | "company"
    public string Name { get; set; } = null!;
    public string? LegalName { get; set; }
    public string? TaxCode { get; set; }
    public string? Address { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
}

public class EInvoiceItemDto
{
    public int LineNumber { get; set; }
    public int LineType { get; set; } = 1;   // 1: hàng hóa/dịch vụ thường
    public string? ItemCode { get; set; }
    public string ItemName { get; set; } = null!;
    public string Unit { get; set; } = "Cái";
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public int? TaxRate { get; set; }   // bắt buộc khi xuất HĐ GTGT: 0/5/8/10, -1, -2
}
