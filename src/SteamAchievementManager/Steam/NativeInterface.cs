using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace SteamAchievementManager.Steam;

// a Steam C++ interface object; its methods are called through the vtable by slot index
// (slot = position of the method in the interface's declaration in the Steamworks headers)
internal sealed class NativeInterface(IntPtr self)
{
    private readonly IntPtr _vtable = Marshal.ReadIntPtr(self);
    private readonly ConcurrentDictionary<int, Delegate> _functions = new();

    // the `this` pointer, passed as the first argument of every (thiscall) method
    public IntPtr Self { get; } = self;

    public T Function<T>(int slot) where T : Delegate =>
        (T)_functions.GetOrAdd(slot, s =>
            Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(_vtable, s * IntPtr.Size)));
}
