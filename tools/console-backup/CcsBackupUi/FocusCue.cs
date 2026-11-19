// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// WB-UX-14: the Workbench's focus marks follow the way the person is using the window, as web pages do with :focus-visible. The keyboard
// focus itself never moves because of this; only whether its mark is drawn:
//   a key press (Tab, an arrow, Enter, Space, Escape, a letter; not Shift, Ctrl, Alt or the Windows key alone)  the focused control shows
//       its mark, and so does every control the keyboard moves to, including the one Windows gives the focus back to when a dialog closed
//       with Escape or Enter
//   a mouse, pen or touch press   the mark goes: a click, or closing a dialog with the mouse, never leaves a red ring on the wordmark, a
//       picture or a button (clicking an empty part of the window clears it too)
// The styles (Workbench.xaml) draw a mark only while the control has the keyboard focus AND FocusCue.Shown is set on it. Selected-item
// marks (the open section's red bar, a chosen backup's outline) are not focus marks and are unchanged.
using System;
using System.Windows;
using System.Windows.Input;

namespace CcsBackup.Ui
{
    public static class FocusCue
    {
        public static readonly DependencyProperty ShownProperty = DependencyProperty.RegisterAttached("Shown", typeof(bool), typeof(FocusCue), new FrameworkPropertyMetadata(false));
        public static bool GetShown(DependencyObject d) => (bool)d.GetValue(ShownProperty);
        public static void SetShown(DependencyObject d, bool value) => d.SetValue(ShownProperty, value);

        static bool installed;
        /// <summary>true after a key press, false after a mouse, pen or touch press (the window starts with no mark until a key is pressed)</summary>
        internal static bool KeyboardInUse { get; private set; }
        static WeakReference marked;

        /// <summary>once per process, on the window's thread (BackupWorkspace's constructor)</summary>
        internal static void Install()
        {
            if (installed) return; installed = true;
            // before the input is routed: a mouse press decides the mark before the click moves the focus; a key press before Tab moves it
            InputManager.Current.PreProcessInput += (s, e) =>
            {
                var ev = e.StagingItem.Input.RoutedEvent;
                if (ev == Keyboard.PreviewKeyDownEvent && e.StagingItem.Input is KeyEventArgs k)
                {
                    var key = k.Key == Key.System ? k.SystemKey : k.Key;
                    if (key == Key.LeftShift || key == Key.RightShift || key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt || key == Key.LWin || key == Key.RWin) return;
                    Use(true);
                }
                else if (ev == Mouse.PreviewMouseDownEvent || ev == Stylus.PreviewStylusDownEvent || ev == UIElement.PreviewTouchDownEvent) Use(false);
            };
            EventManager.RegisterClassHandler(typeof(UIElement), Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(Got), true);
            EventManager.RegisterClassHandler(typeof(ContentElement), Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(Got), true);
        }

        static void Use(bool keyboard)
        {
            if (KeyboardInUse == keyboard) return;
            KeyboardInUse = keyboard;
            Mark(Keyboard.FocusedElement as DependencyObject);
        }

        static void Got(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (!ReferenceEquals(sender, e.NewFocus)) return;   // the class handler runs for each element the event passes; only the focused one counts
            Mark(e.NewFocus as DependencyObject);
        }

        static void Mark(DependencyObject now)
        {
            var old = marked?.Target as DependencyObject;
            if (old != null && !ReferenceEquals(old, now)) old.ClearValue(ShownProperty);
            marked = now == null ? null : new WeakReference(now);
            if (now != null) now.SetValue(ShownProperty, KeyboardInUse);
        }
    }
}
