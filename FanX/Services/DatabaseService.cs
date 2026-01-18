using FanX.Models;
using SqlSugar;


namespace FanX.Services
{
    public class DatabaseService
    {
        private readonly SqlSugarScope _db;

        public DatabaseService(IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrEmpty(connectionString))
            {
                LoggerService.Fatal("Database connection string 'DefaultConnection' not found in configuration.");
                throw new ArgumentNullException(nameof(connectionString), @"Database connection string cannot be null.");
            }

            // Log the absolute path of the SQLite database file for debugging
            var dataSourcePrefix = "Data Source=";
            if (connectionString.StartsWith(dataSourcePrefix, StringComparison.OrdinalIgnoreCase))
            {
                var dbPath = connectionString.Substring(dataSourcePrefix.Length);
                var fullPath = Path.GetFullPath(dbPath);
                LoggerService.Info($"Using SQLite DB file: {fullPath}");
            }

            // Ensure SQLite file exists and is writable
            var dbFilePath = connectionString.Substring(dataSourcePrefix.Length);
            var absoluteDbPath = Path.GetFullPath(dbFilePath);
            var dbDir = Path.GetDirectoryName(absoluteDbPath);
            if (!string.IsNullOrWhiteSpace(dbDir) && !Directory.Exists(dbDir)) Directory.CreateDirectory(dbDir);
            using (new FileStream(absoluteDbPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite)) {}

            _db = new SqlSugarScope(new ConnectionConfig()
            {
                ConnectionString = connectionString,
                DbType = DbType.Sqlite,
                IsAutoCloseConnection = true,
                InitKeyType = InitKeyType.Attribute
            });
            // Configure SQLite pragmas for WAL mode and foreign key enforcement
            _db.Ado.ExecuteCommand("PRAGMA journal_mode=WAL;PRAGMA synchronous=NORMAL;PRAGMA foreign_keys=ON;");

            // initialize the database
            InitializeDatabase();
        }

        public SqlSugarScope Db => _db;

        private void InitializeDatabase()
        {
            try
            {
                LoggerService.Info("Initializing database...");
                
                _db.CodeFirst.InitTables<User>();
                _db.CodeFirst.InitTables<IpmiConfig>();
                _db.CodeFirst.InitTables<SensorData>();
                _db.CodeFirst.InitTables<FanControlRule>();
                _db.CodeFirst.InitTables<AppSetting>();
                _db.CodeFirst.InitTables<NotificationSetting>();
                _db.CodeFirst.InitTables<NotificationRule>();
                _db.CodeFirst.InitTables<NotificationCondition>();
                _db.CodeFirst.InitTables<FanControlCondition>();
                _db.CodeFirst.InitTables<NotificationHistory>();

                LoggerService.Info("Tables 'User', 'IpmiConfig','SensorData', 'FanControlRule', 'AppSetting', 'NotificationSetting', 'NotificationRule', 'NotificationCondition', 'FanControlCondition', and 'NotificationHistory' initialized successfully.");

                if (!_db.Queryable<User>().Any())
                {
                    LoggerService.Info("No users found. Creating default admin user...");
                    var adminUser = new User
                    {
                        Username = "admin",
                        Email = "admin@example.com",
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123"),
                        Role = "Admin",
                        CreatedAt = DateTime.Now,
                        IsActive = true
                    };
                    var result = _db.Insertable(adminUser).ExecuteCommand();
                    LoggerService.Info($"Default admin user created. Rows affected: {result}");
                }
                else
                {
                    LoggerService.Info("Database already contains users. Skipping default user creation.");
                }

                if (!_db.Queryable<IpmiConfig>().Any())
                {
                    LoggerService.Info("No IPMI config found. Creating default configuration...");
                    var defaultConfig = new IpmiConfig
                    {
                        Name = "Default",
                        Host = "",
                        Username = "",
                        Password = ""
                    };
                    _db.Insertable(defaultConfig).ExecuteCommand();
                    LoggerService.Info("Default IPMI configuration created.");
                }

                if (!_db.Queryable<AppSetting>().Any(s => s.Key == "ActiveIpmiConfigId"))
                {
                    var configQuery = _db.Queryable<IpmiConfig>();
                    if (configQuery.Any())
                    {
                        var defaultConfig = configQuery.OrderBy(c => c.Id).First();
                        _db.Insertable(new AppSetting
                        {
                            Key = "ActiveIpmiConfigId",
                            Value = defaultConfig.Id.ToString()
                        }).ExecuteCommand();
                    }
                }

                if (!_db.Queryable<AppSetting>().Any(s => s.Key == "IpmiConfigEnabledInitialized"))
                {
                    if (_db.Queryable<IpmiConfig>().Any(c => !c.IsEnabled))
                    {
                        _db.Updateable<IpmiConfig>()
                            .SetColumns(c => new IpmiConfig { IsEnabled = true })
                            .Where(c => !c.IsEnabled)
                            .ExecuteCommand();
                    }

                    _db.Insertable(new AppSetting
                    {
                        Key = "IpmiConfigEnabledInitialized",
                        Value = "true"
                    }).ExecuteCommand();
                }
                
                if (!_db.Queryable<NotificationSetting>().Any())
                {
                    LoggerService.Info("No Notification settings found. Creating default configuration...");
                    var defaultSettings = new NotificationSetting
                    {
                        WebhookUrl = "",
                        TelegramBotToken = "",
                        TelegramChatId = ""
                    };
                    _db.Insertable(defaultSettings).ExecuteCommand();
                    LoggerService.Info("Default Notification settings created.");
                }

                if (!_db.Queryable<AppSetting>().Any(s => s.Key == "SensorDataIpmiConfigInitialized"))
                {
                    var configQuery = _db.Queryable<IpmiConfig>();
                    if (configQuery.Any() && _db.Queryable<SensorData>().Any(s => s.IpmiConfigId == 0))
                    {
                        var defaultConfig = configQuery.OrderBy(c => c.Id).First();
                        _db.Updateable<SensorData>()
                            .SetColumns(s => new SensorData { IpmiConfigId = defaultConfig.Id })
                            .Where(s => s.IpmiConfigId == 0)
                            .ExecuteCommand();
                    }

                    _db.Insertable(new AppSetting
                    {
                        Key = "SensorDataIpmiConfigInitialized",
                        Value = "true"
                    }).ExecuteCommand();
                }
                
                LoggerService.Info("Database initialization completed successfully.");
            }
            catch (Exception ex)
            {
                LoggerService.Fatal("Database initialization failed.", ex);
                throw;
            }
        }
    }
}
