﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Gibbed.IO;
using ME3Explorer.Packages;
using ME3Explorer.Unreal;

namespace ME3Explorer
{
    public static class TaskExtensions
    {
        //no argument passed to continuation
        public static Task ContinueWithOnUIThread(this Task task, Action<Task> continuationAction)
        {
            return task.ContinueWith(continuationAction, App.SYNCHRONIZATION_CONTEXT);
        }

        //no argument passed to and result returned from continuation
        public static Task<TNewResult> ContinueWithOnUIThread<TNewResult>(this Task task, Func<Task, TNewResult> continuationAction)
        {
            return task.ContinueWith(continuationAction, App.SYNCHRONIZATION_CONTEXT);
        }

        //argument passed to continuationn>
        public static Task ContinueWithOnUIThread<TResult>(this Task<TResult> task, Action<Task<TResult>> continuationAction)
        {
            return task.ContinueWith(continuationAction, App.SYNCHRONIZATION_CONTEXT);
        }

        //argument passed to and result returned from continuation
        public static Task<TNewResult> ContinueWithOnUIThread<TResult, TNewResult>(this Task<TResult> task, Func<Task<TResult>, TNewResult> continuationAction)
        {
            return task.ContinueWith(continuationAction, App.SYNCHRONIZATION_CONTEXT);
        }
    }

    public static class TreeViewExtension
    {
        public static IEnumerable<System.Windows.Forms.TreeNode> FlattenTreeView(this System.Windows.Forms.TreeView tv)
        {
            return tv.Nodes.Cast<System.Windows.Forms.TreeNode>().SelectMany(FlattenTree);

            List<System.Windows.Forms.TreeNode> FlattenTree(System.Windows.Forms.TreeNode rootNode)
            {
                var nodes = new List<System.Windows.Forms.TreeNode> { rootNode };
                foreach (System.Windows.Forms.TreeNode node in rootNode.Nodes)
                {
                    nodes.AddRange(FlattenTree(node));
                }
                return nodes;
            }
        }
    }

    public static class BitArrayExtensions
    {
        public static BitArray Prepend(this BitArray current, BitArray before)
        {
            var bools = new bool[current.Count + before.Count];
            before.CopyTo(bools, 0);
            current.CopyTo(bools, before.Count);
            return new BitArray(bools);
        }

        public static BitArray Append(this BitArray current, BitArray after)
        {
            var bools = new bool[current.Count + after.Count];
            current.CopyTo(bools, 0);
            after.CopyTo(bools, current.Count);
            return new BitArray(bools);
        }

        public static string DebugString(this BitArray array)
        {
            int numTilSpace = 8;
            string print = "";
            foreach (bool b in array)
            {
                print += b ? "1" : "0";
                numTilSpace--;
                if (numTilSpace <= 0)
                {
                    print += " ";
                    numTilSpace = 8;
                }
            }
            return print;
        }
    }

    public static class TreeViewItemExtension
    {
        public static IEnumerable<System.Windows.Controls.TreeViewItem> FlattenTreeView(this System.Windows.Controls.TreeView tv)
        {
            return tv.Items.Cast<System.Windows.Controls.TreeViewItem>().SelectMany(FlattenTree);

            List<System.Windows.Controls.TreeViewItem> FlattenTree(System.Windows.Controls.TreeViewItem rootNode)
            {
                var nodes = new List<System.Windows.Controls.TreeViewItem> { rootNode };
                foreach (System.Windows.Controls.TreeViewItem node in rootNode.Items)
                {
                    nodes.AddRange(FlattenTree(node));
                }
                return nodes;
            }
        }

        /// <summary>
        /// Select specified item in a TreeView
        /// </summary>
        public static void SelectItem(this System.Windows.Controls.TreeViewItem treeView, object item)
        {
            if (treeView.ItemContainerGenerator.ContainerFromItem(item) is System.Windows.Controls.TreeViewItem tvItem)
            {
                tvItem.IsSelected = true;
            }
        }
    }

    public static class TreeViewExtensions
    {
        public static TreeViewItem ContainerFromItemRecursive(this ItemContainerGenerator root, object item)
        {
            if (root.ContainerFromItem(item) is TreeViewItem treeViewItem)
                return treeViewItem;
            foreach (var subItem in root.Items)
            {
                treeViewItem = root.ContainerFromItem(subItem) as TreeViewItem;
                var search = treeViewItem?.ItemContainerGenerator.ContainerFromItemRecursive(item);
                if (search != null)
                    return search;
            }
            return null;
        }
    }

    public static class EnumerableExtensions
    {
        public static int FindOrAdd<T>(this List<T> list, T element)
        {
            int idx = list.IndexOf(element);
            if (idx == -1)
            {
                list.Add(element);
                idx = list.Count - 1;
            }
            return idx;
        }

        public static IEnumerable<T> Concat<T>(this IEnumerable<T> first, T second)
        {
            foreach (T element in first)
            {
                yield return element;
            }
            yield return second;
        }

        /// <summary>
        /// Searches for the specified object and returns the index of its first occurence, or -1 if it is not found
        /// </summary>
        /// <param name="array">The one-dimensional array to search</param>
        /// <param name="value">The object to locate in <paramref name="array" /></param>
        /// <typeparam name="T">The type of the elements of the array.</typeparam>
        /// <exception cref="T:System.ArgumentNullException">
        /// <paramref name="array" /> is null.</exception>
        public static int IndexOf<T>(this T[] array, T value)
        {
            return Array.IndexOf(array, value);
        }

        public static int IndexOf<T>(this LinkedList<T> list, LinkedListNode<T> node)
        {
            LinkedListNode<T> temp = list.First;
            for (int i = 0; i < list.Count; i++)
            {
                if (node == temp)
                {
                    return i;
                }
                temp = temp.Next;
            }
            return -1;
        }

        public static int IndexOf<T>(this LinkedList<T> list, T node)
        {
            LinkedListNode<T> temp = list.First;
            for (int i = 0; i < list.Count; i++)
            {
                if (node.Equals(temp.Value))
                {
                    return i;
                }
                temp = temp.Next;
            }
            return -1;
        }

        public static LinkedListNode<T> Node<T>(this LinkedList<T> list, T node)
        {
            LinkedListNode<T> temp = list.First;
            for (int i = 0; i < list.Count; i++)
            {
                if (node.Equals(temp.Value))
                {
                    return temp;
                }
                temp = temp.Next;
            }
            return null;
        }

        public static void RemoveAt<T>(this LinkedList<T> list, int index)
        {
            list.Remove(list.NodeAt(index));
        }

        public static LinkedListNode<T> NodeAt<T>(this LinkedList<T> list, int index)
        {
            LinkedListNode<T> temp = list.First;
            for (int i = 0; i < list.Count; i++)
            {
                if (i == index)
                {
                    return temp;
                }
                temp = temp.Next;
            }
            throw new ArgumentOutOfRangeException();
        }

        /// <summary>
        /// Overwrites a portion of an array starting at offset with the contents of another array.
        /// Accepts negative indexes
        /// </summary>
        /// <typeparam name="T">Content of array.</typeparam>
        /// <param name="dest">Array to write to</param>
        /// <param name="offset">Start index in dest. Can be negative (eg. last element is -1)</param>
        /// <param name="source">data to write to dest</param>
        public static void OverwriteRange<T>(this IList<T> dest, int offset, IList<T> source)
        {
            if (offset < 0)
            {
                offset = dest.Count + offset;
                if (offset < 0)
                {
                    throw new IndexOutOfRangeException("Attempt to write before the beginning of the array.");
                }
            }
            if (offset + source.Count > dest.Count)
            {
                throw new IndexOutOfRangeException("Attempt to write past the end of the array.");
            }
            for (int i = 0; i < source.Count; i++)
            {
                dest[offset + i] = source[i];
            }
        }

        public static T[] TypedClone<T>(this T[] src)
        {
            return (T[])src.Clone();
        }

        /// <summary>
        /// Creates a shallow copy
        /// </summary>
        public static List<T> Clone<T>(this IEnumerable<T> src)
        {
            return new List<T>(src);
        }

        //https://stackoverflow.com/a/26880541
        /// <summary>
        /// Searches for the specified array and returns the index of its first occurence, or -1 if it is not found
        /// </summary>
        /// <param name="haystack">The one-dimensional array to search</param>
        /// <param name="needle">The object to locate in <paramref name="haystack" /></param>
        /// <param name="start">The index to start searching at</param>
        public static int IndexOfArray<T>(this T[] haystack, T[] needle, int start = 0) where T : IEquatable<T>
        {
            var len = needle.Length;
            var limit = haystack.Length - len;
            for (var i = start; i <= limit; i++)
            {
                var k = 0;
                for (; k < len; k++)
                {
                    if (!needle[k].Equals(haystack[i + k])) break;
                }
                if (k == len) return i;
            }
            return -1;
        }
    }

    public static class StringExtensions
    {
        public static bool isNumericallyEqual(this string first, string second)
        {
            return double.TryParse(first, out double a)
                && double.TryParse(second, out double b)
                && (Math.Abs(a - b) < double.Epsilon);
        }

        //based on algorithm described here: http://www.codeproject.com/Articles/13525/Fast-memory-efficient-Levenshtein-algorithm
        public static int LevenshteinDistance(this string a, string b)
        {
            int n = a.Length;
            int m = b.Length;
            if (n == 0)
            {
                return m;
            }
            if (m == 0)
            {
                return n;
            }

            var v1 = new int[m + 1];
            for (int i = 0; i <= m; i++)
            {
                v1[i] = i;
            }

            for (int i = 1; i <= n; i++)
            {
                int[] v0 = v1;
                v1 = new int[m + 1];
                v1[0] = i;
                for (int j = 1; j <= m; j++)
                {
                    int above = v1[j - 1] + 1;
                    int left = v0[j] + 1;
                    int cost;
                    if (j > m || j > n)
                    {
                        cost = 1;
                    }
                    else
                    {
                        cost = a[j - 1] == b[j - 1] ? 0 : 1;
                    }
                    cost += v0[j - 1];
                    v1[j] = Math.Min(above, Math.Min(left, cost));

                }
            }

            return v1[m];
        }

        public static bool FuzzyMatch(this IEnumerable<string> words, string word, double threshold = 0.75)
        {
            foreach (string s in words)
            {
                int dist = s.LevenshteinDistance(word);
                if (1 - (double)dist / Math.Max(s.Length, word.Length) > threshold)
                {
                    return true;
                }
            }
            return false;
        }
    }

    public static class WPFExtensions
    {
        /// <summary>
        /// Binds a property
        /// </summary>
        /// <param name="bound">object to create binding on</param>
        /// <param name="boundProp">property to create binding on</param>
        /// <param name="source">object being bound to</param>
        /// <param name="sourceProp">property being bound to</param>
        /// <param name="converter">optional value converter</param>
        /// <param name="parameter">optional value converter parameter</param>
        public static void bind(this FrameworkElement bound, DependencyProperty boundProp, FrameworkElement source, string sourceProp,
            IValueConverter converter = null, object parameter = null)
        {
            Binding b = new Binding { Source = source, Path = new PropertyPath(sourceProp) };
            if (converter != null)
            {
                b.Converter = converter;
                b.ConverterParameter = parameter;
            }
            bound.SetBinding(boundProp, b);
        }

        /// <summary>
        /// Starts a DoubleAnimation for a specified animated property on this element
        /// </summary>
        /// <param name="target">element to perform animation on</param>
        /// <param name="dp">The property to animate, which is specified as a dependency property identifier</param>
        /// <param name="toValue">The destination value of the animation</param>
        /// <param name="duration">The duration of the animation, in milliseconds</param>
        public static void BeginDoubleAnimation(this UIElement target, DependencyProperty dp, double toValue, int duration)
        {
            target.BeginAnimation(dp, new DoubleAnimation(toValue, TimeSpan.FromMilliseconds(duration)));
        }

        public static void AppendLine(this TextBoxBase box, string text)
        {
            box.AppendText(text + Environment.NewLine);
            box.ScrollToEnd();
        }

        public static BitmapImage ToBitmapImage(this System.Drawing.Bitmap bitmap)
        {
            MemoryStream memory = new MemoryStream();
            bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Bmp);
            memory.Position = 0;
            BitmapImage bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.StreamSource = memory;
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.EndInit();
            bitmapImage.Freeze();
            return bitmapImage;
        }
    }

    public static class ExternalExtensions
    {
        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")]
        static extern bool ShowWindowAsync(IntPtr hWnd, int cmdShow);
        [DllImport("user32.dll")]
        static extern int SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, int lParam);

        public static void RestoreAndBringToFront(this Window window)
        {
            WindowInteropHelper helper = new WindowInteropHelper(window);
            //if window is minimized
            if (IsIconic(helper.Handle))
            {
                //SW_RESTORE = 9;
                ShowWindowAsync(helper.Handle, 9);
            }
            SetForegroundWindow(helper.Handle);
        }

        public static void RestoreAndBringToFront(this System.Windows.Forms.Form form)
        {
            //if window is minimized
            if (IsIconic(form.Handle))
            {
                //SW_RESTORE = 9;
                ShowWindowAsync(form.Handle, 9);
            }
            SetForegroundWindow(form.Handle);
        }

        public static bool IsForegroundWindow(this System.Windows.Forms.Form form)
        {
            return GetForegroundWindow() == form.Handle;
        }

        public static bool IsForegroundWindow(this Window window)
        {
            WindowInteropHelper helper = new WindowInteropHelper(window);
            return GetForegroundWindow() == helper.Handle;
        }

        //modified from https://social.msdn.microsoft.com/Forums/vstudio/en-US/df4db537-a201-4ab4-bb7e-db38a5c2b6e0/wpf-equivalent-of-winforms-controldrawtobitmap
        public static BitmapSource DrawToBitmapSource(this Visual target)
        {
            Rect bounds = VisualTreeHelper.GetDescendantBounds(target);

            RenderTargetBitmap renderTarget = new RenderTargetBitmap((int)bounds.Width, (int)bounds.Height, 96, 96, PixelFormats.Pbgra32);

            DrawingVisual visual = new DrawingVisual();

            using (DrawingContext context = visual.RenderOpen())
            {
                VisualBrush visualBrush = new VisualBrush(target);
                context.DrawRectangle(visualBrush, null, new Rect(new Point(), bounds.Size));
            }

            renderTarget.Render(visual);
            return renderTarget;
        }

        public static BitmapSource DrawToBitmapSource(this System.Windows.Forms.Control control)
        {
            const int WM_PRINT = 0x317, PRF_CLIENT = 4,
            PRF_CHILDREN = 0x10, PRF_NON_CLIENT = 2,
            COMBINED_PRINTFLAGS = PRF_CLIENT | PRF_CHILDREN | PRF_NON_CLIENT;

            System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(control.Width, control.Height);
            System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(bitmap);

            // paint control onto graphics
            IntPtr hWnd = control.Handle;
            IntPtr hDC = graphics.GetHdc();
            SendMessage(hWnd, WM_PRINT, hDC, COMBINED_PRINTFLAGS);
            graphics.ReleaseHdc(hDC);

            return bitmap.ToBitmapImage();
        }
    }

    //http://stackoverflow.com/a/11433814/1968930
    public static class HyperlinkExtensions
    {
        public static bool GetIsExternal(DependencyObject obj)
        {
            return (bool)obj.GetValue(IsExternalProperty);
        }

        public static void SetIsExternal(DependencyObject obj, bool value)
        {
            obj.SetValue(IsExternalProperty, value);
        }
        public static readonly DependencyProperty IsExternalProperty =
            DependencyProperty.RegisterAttached("IsExternal", typeof(bool), typeof(HyperlinkExtensions), new PropertyMetadata(false, OnIsExternalChanged));

        private static void OnIsExternalChanged(object sender, DependencyPropertyChangedEventArgs args)
        {
            var hyperlink = sender as Hyperlink;

            if ((bool)args.NewValue)
                hyperlink.RequestNavigate += Hyperlink_RequestNavigate;
            else
                hyperlink.RequestNavigate -= Hyperlink_RequestNavigate;
        }

        private static void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(e.Uri.AbsoluteUri);
            e.Handled = true;
        }
    }

    public static class IOExtensions
    {
        public static void WriteStringASCII(this Stream stream, string value)
        {
            stream.WriteValueS32(value.Length + 1);
            stream.WriteStringZ(value, Encoding.ASCII);
        }

        public static void WriteStringUnicode(this Stream stream, string value)
        {
            if (value.Length > 0)
            {
                stream.WriteValueS32(-(value.Length + 1));
                stream.WriteStringZ(value, Encoding.Unicode);
            }
            else
            {
                stream.WriteValueS32(0);
            }
        }

        public static void WriteStream(this Stream stream, MemoryStream value)
        {
            value.WriteTo(stream);
        }

        /// <summary>
        /// Copies the inputstream to the outputstream, for the specified amount of bytes
        /// </summary>
        /// <param name="input">Stream to copy from</param>
        /// <param name="output">Stream to copy to</param>
        /// <param name="bytes">The number of bytes to copy</param>
        public static void CopyToEx(this Stream input, Stream output, int bytes)
        {
            var buffer = new byte[32768];
            int read;
            while (bytes > 0 &&
                   (read = input.Read(buffer, 0, Math.Min(buffer.Length, bytes))) > 0)
            {
                output.Write(buffer, 0, read);
                bytes -= read;
            }
        }
    }

    public static class UnrealExtensions
    {
        /// <summary>
        /// Converts an ME3Explorer index to an unreal index. (0 and up get incremented, less than zero stays the same)
        /// </summary>
        public static int ToUnrealIdx(this int i)
        {
            return i >= 0 ? i + 1 : i;
        }

        /// <summary>
        /// Converts an unreal index to an ME3Explorer index. (greater than zero gets decremented, less than zero stays the same,
        /// and 0 returns null)
        /// </summary>
        public static int? FromUnrealIdx(this int i)
        {
            if (i == 0)
            {
                return null;
            }
            return i > 0 ? i - 1 : i;
        }

        /// <summary>
        /// Checks if this object is of a specific generic type (e.g. List&lt;IntProperty&gt;)
        /// </summary>
        /// <param name="typeToCheck">typeof() of the item you are checking</param>
        /// <param name="genericType">typeof() of the value you are checking against</param>
        /// <returns>True if type matches, false otherwise</returns>
        public static bool IsOfGenericType(this Type typeToCheck, Type genericType)
        {
            return typeToCheck.IsOfGenericType(genericType, out Type _);
        }

        /// <summary>
        /// Checks if this object is of a specific generic type (e.g. List&lt;IntProperty&gt;)
        /// </summary>
        /// <param name="typeToCheck">typeof() of the item you are checking</param>
        /// <param name="genericType">typeof() of the value you are checking against</param>
        /// <param name="concreteGenericType">Concrete type output if this result is true</param>
        /// <returns>True if type matches, false otherwise</returns>
        public static bool IsOfGenericType(this Type typeToCheck, Type genericType, out Type concreteGenericType)
        {
            while (true)
            {
                concreteGenericType = null;

                if (genericType == null)
                    throw new ArgumentNullException(nameof(genericType));

                if (!genericType.IsGenericTypeDefinition)
                    throw new ArgumentException("The definition needs to be a GenericTypeDefinition", nameof(genericType));

                if (typeToCheck == null || typeToCheck == typeof(object))
                    return false;

                if (typeToCheck == genericType)
                {
                    concreteGenericType = typeToCheck;
                    return true;
                }

                if ((typeToCheck.IsGenericType ? typeToCheck.GetGenericTypeDefinition() : typeToCheck) == genericType)
                {
                    concreteGenericType = typeToCheck;
                    return true;
                }

                if (genericType.IsInterface)
                    foreach (var i in typeToCheck.GetInterfaces())
                        if (i.IsOfGenericType(genericType, out concreteGenericType))
                            return true;

                typeToCheck = typeToCheck.BaseType;
            }
        }
    }

    public static class EnumExtensions
    {
        public static T[] GetValues<T>() where T : Enum
        {
            return (T[])Enum.GetValues(typeof(T));
        }
    }

    public static class EnumHelper<T> where T : struct
    {
        // ReSharper disable StaticFieldInGenericType
        private static readonly Enum[] Values;
        // ReSharper restore StaticFieldInGenericType
        private static readonly T DefaultValue;

        static EnumHelper()
        {
            var type = typeof(T);
            if (type.IsSubclassOf(typeof(Enum)) == false)
            {
                throw new ArgumentException();
            }
            Values = Enum.GetValues(type).Cast<Enum>().ToArray();
            DefaultValue = default;
        }

        public static T[] MaskToList(Enum mask, bool ignoreDefault = true)
        {
            var q = Values.Where(mask.HasFlag);
            if (ignoreDefault)
            {
                q = q.Where(v => !v.Equals(DefaultValue));
            }
            return q.Cast<T>().ToArray();
        }
    }
}
