using SqlSugar;

namespace FanX.Models
{
    public class IpmiConfig
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int Id { get; set; }
        public string? Name { get; set; }
        public bool IsEnabled { get; set; } = true;
        public string? Host { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }

        public IpmiConfig Clone()
        {
            return new IpmiConfig
            {
                Id = Id,
                Name = Name,
                Host = Host,
                Username = Username,
                Password = Password,
                IsEnabled = IsEnabled
            };
        }
    }
} 
