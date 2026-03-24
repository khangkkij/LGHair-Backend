using System;
using System.Collections.Generic;

namespace MinhTamHair.Models;

public partial class Refreshtoken
{
    public int Id { get; set; }

    public string? UserId { get; set; }

    public string? Token { get; set; }

    public DateTime? IssuedAt { get; set; }

    public DateTime? ExpiredAt { get; set; }
}
