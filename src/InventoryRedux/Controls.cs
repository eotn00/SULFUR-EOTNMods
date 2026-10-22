using UnityEngine.InputSystem;

namespace LocalSulfur.UnifiedInventory
{
    internal enum Modifier { Alt, Control, Shift }
    internal enum LayoutButton { Left, Middle }
    internal static class Controls
    {
        internal static bool Held(Modifier value)
        {
            var k=Keyboard.current;if(k==null)return false;
            if(value==Modifier.Control)return k.leftCtrlKey.isPressed||k.rightCtrlKey.isPressed;
            if(value==Modifier.Shift)return k.leftShiftKey.isPressed||k.rightShiftKey.isPressed;
            return k.leftAltKey.isPressed||k.rightAltKey.isPressed;
        }
        internal static bool LayoutHeld {get{return Held(Plugin.Instance.Settings.LayoutModifier.Value);}}
        internal static bool SplitHeld {get{return Held(Plugin.Instance.Settings.SplitModifier.Value)||Held(Plugin.Instance.Settings.SingleModifier.Value);}}
        internal static bool DragHeld
        {get{var m=Mouse.current;return m!=null&&(Plugin.Instance.Settings.DragButton.Value==LayoutButton.Left?m.leftButton.isPressed:m.middleButton.isPressed);}}
        internal static bool DragStarted
        {get{var m=Mouse.current;return m!=null&&(Plugin.Instance.Settings.DragButton.Value==LayoutButton.Left?m.leftButton.wasPressedThisFrame:m.middleButton.wasPressedThisFrame);}}
        internal static bool LockPressed
        {get{var k=Keyboard.current;var key=Plugin.Instance.Settings.LockKey.Value;return k!=null&&key!=Key.None&&k[key].wasPressedThisFrame;}}
    }
}
