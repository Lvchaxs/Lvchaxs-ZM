using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Lvchaxs_ZH.Models
{
    public class Account : INotifyPropertyChanged
    {
        private int _id;
        private string _uid = "";
        private int _level;
        private string _nickname = "";
        private string _encryptedUsername = "";
        private string _encryptedPassword = "";
        private bool _isPasswordVisible = false;
        private bool _isMarked = false;
        private bool _isPinned = false;
        private DateTime _createdTime;
        private DateTime? _pinnedTime;

        [JsonPropertyName("id")]
        public int Id
        {
            get => _id;
            set
            {
                _id = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyName("isMarked")]
        public bool IsMarked
        {
            get => _isMarked;
            set
            {
                if (_isMarked != value)
                {
                    _isMarked = value;
                    OnPropertyChanged();
                }
            }
        }

        [JsonPropertyName("isPinned")]
        public bool IsPinned
        {
            get => _isPinned;
            set
            {
                if (_isPinned != value)
                {
                    _isPinned = value;
                    OnPropertyChanged();
                }
            }
        }

        [JsonPropertyName("createdTime")]
        public DateTime CreatedTime
        {
            get => _createdTime;
            set
            {
                _createdTime = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyName("pinnedTime")]
        public DateTime? PinnedTime
        {
            get => _pinnedTime;
            set
            {
                _pinnedTime = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyName("uid")]
        public string Uid
        {
            get => _uid;
            set
            {
                _uid = value ?? "";
                OnPropertyChanged();
            }
        }

        [JsonPropertyName("level")]
        public int Level
        {
            get => _level;
            set
            {
                _level = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(WorldLevel));
            }
        }

        [JsonPropertyName("nickname")]
        public string Nickname
        {
            get => _nickname;
            set
            {
                _nickname = value ?? "";
                OnPropertyChanged();
            }
        }

        [JsonPropertyName("encryptedUsername")]
        public string EncryptedUsername
        {
            get => _encryptedUsername;
            set => _encryptedUsername = value ?? "";
        }

        [JsonPropertyName("encryptedPassword")]
        public string EncryptedPassword
        {
            get => _encryptedPassword;
            set => _encryptedPassword = value ?? "";
        }

        [JsonIgnore]
        public string Username
        {
            get => Services.AccountEncryptionService.Decrypt(_encryptedUsername);
            set
            {
                _encryptedUsername = Services.AccountEncryptionService.Encrypt(value ?? "");
                OnPropertyChanged();
                OnPropertyChanged(nameof(MaskedUsername));
            }
        }

        [JsonIgnore]
        public string Password
        {
            get => Services.AccountEncryptionService.Decrypt(_encryptedPassword);
            set
            {
                _encryptedPassword = Services.AccountEncryptionService.Encrypt(value ?? "");
                OnPropertyChanged();
                OnPropertyChanged(nameof(MaskedPassword));
            }
        }

        [JsonIgnore]
        public int WorldLevel => CalculateWorldLevel(_level);

        [JsonIgnore]
        public string MaskedUsername => GetMaskedUsername();

        [JsonIgnore]
        public string MaskedPassword => GetMaskedPassword();

        [JsonIgnore]
        public bool IsPasswordVisible
        {
            get => _isPasswordVisible;
            set
            {
                _isPasswordVisible = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MaskedPassword));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // 构造函数
        public Account()
        {
            _createdTime = DateTime.Now;
        }

        public Account(int id, string uid, int level, string nickname, string username, string password)
        {
            _id = id;
            _uid = uid ?? "";
            _level = level;
            _nickname = nickname ?? "";
            _encryptedUsername = Services.AccountEncryptionService.Encrypt(username ?? "");
            _encryptedPassword = Services.AccountEncryptionService.Encrypt(password ?? "");
            _createdTime = DateTime.Now;
        }

        // 设置置顶时间
        public void SetPinned(bool isPinned)
        {
            IsPinned = isPinned;
            if (isPinned)
            {
                PinnedTime = DateTime.Now;
            }
            else
            {
                PinnedTime = null;
            }
        }

        // 私有辅助方法
        private static int CalculateWorldLevel(int level)
        {
            if (level >= 58) return 9;
            if (level >= 55) return 8;
            if (level >= 50) return 7;
            if (level >= 45) return 6;
            if (level >= 40) return 5;
            if (level >= 35) return 4;
            if (level >= 30) return 3;
            if (level >= 25) return 2;
            if (level >= 20) return 1;
            return 0;
        }

        private string GetMaskedUsername()
        {
            if (string.IsNullOrEmpty(Username))
                return string.Empty;

            if (Username.Length == 11 && Username.All(char.IsDigit))
            {
                return $"{Username.Substring(0, 3)}●●●●{Username.Substring(Username.Length - 4)}";
            }

            if (Username.Contains("@"))
            {
                var atIndex = Username.IndexOf('@');
                if (atIndex > 0)
                {
                    var prefix = Username.Substring(0, Math.Min(3, atIndex));
                    var suffix = Username.Substring(atIndex);
                    return $"{prefix}●●●●{suffix}";
                }
            }

            if (Username.Length > 8)
            {
                return $"{Username.Substring(0, 4)}●●●●{Username.Substring(Username.Length - 4)}";
            }

            return Username;
        }

        private string GetMaskedPassword()
        {
            string password = Password;
            if (string.IsNullOrEmpty(password))
                return "";

            return _isPasswordVisible ? password : new string('●', Math.Min(password.Length, 12));
        }
    }
}