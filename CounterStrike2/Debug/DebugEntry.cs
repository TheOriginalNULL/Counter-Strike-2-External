using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CounterStrike2.Debug
{
    /// <summary>One row in the debug address table — updates the UI automatically via INPC.</summary>
    public sealed class DebugEntry : INotifyPropertyChanged
    {
        public string Name   { get; }
        public string Module { get; }

        private IntPtr _address;
        public IntPtr Address
        {
            get => _address;
            set
            {
                if (_address == value) return;
                _address = value;
                Notify();
                Notify(nameof(AddressText));
                Notify(nameof(IsValid));
            }
        }

        public bool   IsValid     => _address != IntPtr.Zero;
        public string AddressText => _address == IntPtr.Zero
            ? "not resolved"
            : $"0x{_address.ToInt64():X16}";

        public DebugEntry(string name, string module) { Name = name; Module = module; }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
