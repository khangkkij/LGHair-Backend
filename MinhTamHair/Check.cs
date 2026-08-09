using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using MinhTamHair.Models;
using Microsoft.Extensions.DependencyInjection;

namespace CheckDB
{
    class Program
    {
        static void Main(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<MinhTamHairContext>();
            optionsBuilder.UseSqlServer("Server=localhost;Database=MinhTamHair;Trusted_Connection=True;TrustServerCertificate=True;");
            using (var context = new MinhTamHairContext(optionsBuilder.Options))
            {
                var v = context.Vouchers.FirstOrDefault(x => x.MaCode == "VD50");
                if (v != null)
                {
                    Console.WriteLine("VD50: NgayBatDau: " + v.NgayBatDau + ", NgayKetThuc: " + v.NgayKetThuc);
                }
                else
                {
                    Console.WriteLine("VD50 not found");
                }
            }
        }
    }
}
