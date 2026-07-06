using System;
using System.Collections.Generic;

namespace MinhTamHair.Services;

public class EInvoiceOptions
{
    public string BaseUrl { get; set; } = null!;
    public string Username { get; set; } = null!;
    public string Password { get; set; } = null!;
    public string ProviderAccountId { get; set; } = null!;
    public string TemplateCodeSale { get; set; } = null!;
    public string TemplateCodeVat { get; set; } = null!;
    public string InvoiceSeries { get; set; } = null!;
}
