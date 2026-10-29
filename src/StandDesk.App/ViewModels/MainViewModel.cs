using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace StandDesk.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private string _height = "—", _status = "Not connected", _sit = "74.12", _stand = "116.87", _sitKey = "", _standKey = "", _connection = "Disconnected";
    private bool _canMove, _canEdit = true;
    public string Height { get => _height; set => Set(ref _height, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
    public string Connection { get => _connection; set => Set(ref _connection, value); }
    public string Sit { get => _sit; set => Set(ref _sit, value); }
    public string Stand { get => _stand; set => Set(ref _stand, value); }
    public string SitKey { get => _sitKey; set => Set(ref _sitKey, value); }
    public string StandKey { get => _standKey; set => Set(ref _standKey, value); }
    public bool CanMove { get => _canMove; set => Set(ref _canMove, value); }
    public bool CanEdit { get => _canEdit; set => Set(ref _canEdit, value); }
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; PropertyChanged?.Invoke(this, new(name)); }
}
