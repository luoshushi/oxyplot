// --------------------------------------------------------------------------------------------------------------------
// <copyright file="Plot.cs" company="OxyPlot">
//   http://oxyplot.codeplex.com, license: Ms-PL
// </copyright>
// --------------------------------------------------------------------------------------------------------------------

namespace OxyPlot.Silverlight
{
    using System;
    using System.Collections.ObjectModel;
    using System.Linq;
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Data;
    using System.Windows.Input;
    using System.Windows.Markup;
    using System.Windows.Media;
    using System.Windows.Media.Imaging;

    /// <summary>
    /// The Silverlight Plot control.
    /// </summary>
    [ContentProperty("Series")]
    [TemplatePart(Name = PartGrid, Type = typeof(Grid))]
    public partial class Plot : Control, IPlotControl
    {
        #region Constants and Fields

        /// <summary>
        ///   The Grid PART constant.
        /// </summary>
        private const string PartGrid = "PART_Grid";

        /// <summary>
        /// The invalidate lock.
        /// </summary>
        private readonly object invalidateLock = new object();

        /// <summary>
        /// The model lock.
        /// </summary>
        private readonly object modelLock = new object();

        /// <summary>
        /// The rendering lock.
        /// </summary>
        private readonly object renderingLock = new object();

        /// <summary>
        /// The update model and visuals lock.
        /// </summary>
        private readonly object updateModelAndVisualsLock = new object();

        /// <summary>
        ///   The tracker definitions.
        /// </summary>
        private readonly ObservableCollection<TrackerDefinition> trackerDefinitions;

        /// <summary>
        ///   The canvas.
        /// </summary>
        private Canvas canvas;

        /// <summary>
        /// The current model.
        /// </summary>
        private PlotModel currentModel;

        /// <summary>
        ///   The current tracker.
        /// </summary>
        private FrameworkElement currentTracker;

        /// <summary>
        ///   The grid.
        /// </summary>
        private Grid grid;

        /// <summary>
        ///   The internal model.
        /// </summary>
        private PlotModel internalModel;

        /// <summary>
        ///   Flag to update data when the plot has been invalidated.
        /// </summary>
        private bool invalidateUpdatesData;

        /// <summary>
        ///   The is plot invalidated.
        /// </summary>
        private bool isPlotInvalidated;

        /// <summary>
        ///   The mouse manipulator.
        /// </summary>
        private ManipulatorBase mouseManipulator;

        /// <summary>
        ///   The overlays.
        /// </summary>
        private Canvas overlays;

        /// <summary>
        ///   The touch down point.
        /// </summary>
        private Point touchDownPoint;

        /// <summary>
        ///   The touch pan manipulator.
        /// </summary>
        private PanManipulator touchPan;

        /// <summary>
        ///   The touch zoom manipulator.
        /// </summary>
        private ZoomManipulator touchZoom;

        /// <summary>
        ///   The zoom control.
        /// </summary>
        private ContentControl zoomControl;

        #endregion

        #region Constructors and Destructors

        /// <summary>
        ///   Initializes a new instance of the <see cref="Plot" /> class.
        /// </summary>
        public Plot()
        {
            this.DefaultStyleKey = typeof(Plot);

            this.trackerDefinitions = new ObservableCollection<TrackerDefinition>();
            this.Loaded += this.OnLoaded;
            this.SizeChanged += this.OnSizeChanged;
            CompositionTarget.Rendering += this.CompositionTargetRendering;

            // http://nuggets.hammond-turner.org.uk/2009/01/quickie-simulating-datacontextchanged.html
            // TODO: doesn't work?
            this.SetBinding(DataContextWatcherProperty, new Binding());
        }

        #endregion

        #region Enums

        /// <summary>
        /// The mouse button.
        /// </summary>
        public enum MouseButton
        {
            /// <summary>
            ///   The left.
            /// </summary>
            Left,

            /// <summary>
            ///   The middle.
            /// </summary>
            Middle,

            /// <summary>
            ///   The right.
            /// </summary>
            Right,

            /// <summary>
            ///   The x button 1.
            /// </summary>
            XButton1,

            /// <summary>
            ///   The x button 2.
            /// </summary>
            XButton2
        }

        #endregion

        #region Public Properties

        /// <summary>
        ///   Gets the actual model.
        /// </summary>
        /// <value> The actual model. </value>
        public PlotModel ActualModel
        {
            get
            {
                return this.Model ?? this.internalModel;
            }
        }

        /// <summary>
        ///   Gets the tracker definitions.
        /// </summary>
        /// <value> The tracker definitions. </value>
        public ObservableCollection<TrackerDefinition> TrackerDefinitions
        {
            get
            {
                return this.trackerDefinitions;
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Gets the axes from a point.
        /// </summary>
        /// <param name="pt">
        /// The point. 
        /// </param>
        /// <param name="xaxis">
        /// The x-axis. 
        /// </param>
        /// <param name="yaxis">
        /// The y-axis. 
        /// </param>
        public void GetAxesFromPoint(ScreenPoint pt, out Axis xaxis, out Axis yaxis)
        {
            if (this.ActualModel != null)
            {
                this.ActualModel.GetAxesFromPoint(pt, out xaxis, out yaxis);
            }
            else
            {
                xaxis = null;
                yaxis = null;
            }
        }

        /// <summary>
        /// Gets the series that is nearest the specified point (in screen coordinates).
        /// </summary>
        /// <param name="pt">
        /// The point. 
        /// </param>
        /// <param name="limit">
        /// The maximum distance, if this is exceeded the method will return null. 
        /// </param>
        /// <returns>
        /// The closest DataSeries 
        /// </returns>
        public Series GetSeriesFromPoint(ScreenPoint pt, double limit)
        {
            return this.ActualModel != null ? this.ActualModel.GetSeriesFromPoint(pt, limit) : null;
        }

        /// <summary>
        /// Hides the tracker.
        /// </summary>
        public void HideTracker()
        {
            if (this.currentTracker != null)
            {
                this.overlays.Children.Remove(this.currentTracker);
                this.currentTracker = null;
            }
        }

        /// <summary>
        /// Hides the zoom rectangle.
        /// </summary>
        public void HideZoomRectangle()
        {
            this.zoomControl.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Invalidate the plot (not blocking the UI thread)
        /// </summary>
        /// <param name="updateData">
        /// The update Data. 
        /// </param>
        public void InvalidatePlot(bool updateData = true)
        {
            lock (this.invalidateLock)
            {
                this.isPlotInvalidated = true;
                this.invalidateUpdatesData = this.invalidateUpdatesData || updateData;
            }
        }

        /// <summary>
        /// When overridden in a derived class, is invoked whenever application code or internal processes (such as a rebuilding layout pass) call <see cref="M:System.Windows.Controls.Control.ApplyTemplate"/> . In simplest terms, this means the method is called just before a UI element displays in an application. For more information, see Remarks.
        /// </summary>
        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            this.grid = this.GetTemplateChild(PartGrid) as Grid;
            if (this.grid == null)
            {
                return;
            }

            this.canvas = new Canvas();
            this.grid.Children.Add(this.canvas);
            this.canvas.UpdateLayout();

            this.overlays = new Canvas();
            this.grid.Children.Add(this.overlays);

            this.zoomControl = new ContentControl();
            this.overlays.Children.Add(this.zoomControl);
        }

        /// <summary>
        /// Pans the specified axis.
        /// </summary>
        /// <param name="axis">
        /// The axis. 
        /// </param>
        /// <param name="ppt">
        /// The previous point (screen coordinates). 
        /// </param>
        /// <param name="cpt">
        /// The current point (screen coordinates). 
        /// </param>
        public void Pan(Axis axis, ScreenPoint ppt, ScreenPoint cpt)
        {
            axis.Pan(ppt, cpt);
            this.InvalidatePlot(false);
        }

        /// <summary>
        /// Refresh the plot immediately (not blocking UI thread)
        /// </summary>
        /// <param name="updateData">
        /// if set to <c>true</c> , the data collections will be updated. 
        /// </param>
        public void RefreshPlot(bool updateData)
        {
            // don't block ui thread on silverlight
            this.InvalidatePlot(updateData);
        }

        /// <summary>
        /// Resets the specified axis.
        /// </summary>
        /// <param name="axis">
        /// The axis. 
        /// </param>
        public void Reset(Axis axis)
        {
            axis.Reset();
        }

        /// <summary>
        /// Reset all axes.
        /// </summary>
        public void ResetAllAxes()
        {
            foreach (var a in this.ActualModel.Axes)
            {
                a.Reset();
            }

            this.InvalidatePlot(false);
        }

        /// <summary>
        /// Saves the plot as a bitmap.
        /// </summary>
        /// <param name="fileName">
        /// Name of the file. 
        /// </param>
        public void SaveBitmap(string fileName)
        {
            throw new NotImplementedException();

            // todo: Use imagetools.codeplex.com
            // or http://windowspresentationfoundation.com/Blend4Book/SaveTheImage.txt

            // var bmp = this.ToBitmap();

            // var encoder = new PngBitmapEncoder();
            // encoder.Frames.Add(BitmapFrame.Create(bmp));

            // using (FileStream s = File.Create(fileName))
            // {
            // encoder.Save(s);
            // }
        }

        /// <summary>
        /// Sets the cursor type.
        /// </summary>
        /// <param name="cursorType">
        /// The cursor type. 
        /// </param>
        public void SetCursorType(CursorType cursorType)
        {
            switch (cursorType)
            {
                case CursorType.Pan:
                    this.Cursor = this.PanCursor;
                    break;
                case CursorType.ZoomRectangle:
                    this.Cursor = this.ZoomRectangleCursor;
                    break;
                case CursorType.ZoomHorizontal:
                    this.Cursor = this.ZoomHorizontalCursor;
                    break;
                case CursorType.ZoomVertical:
                    this.Cursor = this.ZoomVerticalCursor;
                    break;
                default:
                    this.Cursor = Cursors.Arrow;
                    break;
            }
        }

        /// <summary>
        /// Shows the tracker.
        /// </summary>
        /// <param name="trackerHitResult">
        /// The tracker data. 
        /// </param>
        public void ShowTracker(TrackerHitResult trackerHitResult)
        {
            if (trackerHitResult != null)
            {
                var ts = trackerHitResult.Series as ITrackableSeries;
                ControlTemplate trackerTemplate = this.DefaultTrackerTemplate;
                if (ts != null && !string.IsNullOrEmpty(ts.TrackerKey))
                {
                    TrackerDefinition match = this.TrackerDefinitions.FirstOrDefault(t => t.TrackerKey == ts.TrackerKey);
                    if (match != null)
                    {
                        trackerTemplate = match.TrackerTemplate;
                    }
                }

                var tracker = new ContentControl { Template = trackerTemplate };

                if (tracker != this.currentTracker)
                {
                    this.HideTracker();
                    if (trackerTemplate != null)
                    {
                        this.overlays.Children.Add(tracker);
                        this.currentTracker = tracker;
                    }
                }

                if (this.currentTracker != null)
                {
                    this.currentTracker.DataContext = trackerHitResult;
                }
            }
            else
            {
                this.HideTracker();
            }
        }

        /// <summary>
        /// Shows the zoom rectangle.
        /// </summary>
        /// <param name="r">
        /// The rectangle. 
        /// </param>
        public void ShowZoomRectangle(OxyRect r)
        {
            this.zoomControl.Width = r.Width;
            this.zoomControl.Height = r.Height;
            Canvas.SetLeft(this.zoomControl, r.Left);
            Canvas.SetTop(this.zoomControl, r.Top);
            this.zoomControl.Template = this.ZoomRectangleTemplate;
            this.zoomControl.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Renders the plot to a bitmap.
        /// </summary>
        /// <returns>
        /// A bitmap. 
        /// </returns>
        public WriteableBitmap ToBitmap()
        {
            throw new NotImplementedException();

            // var bmp = new RenderTargetBitmap(
            // (int)this.ActualWidth, (int)this.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            // bmp.Render(this);
            // return bmp;
        }

        /// <summary>
        /// Renders the plot to xaml.
        /// </summary>
        /// <returns>
        /// The to xaml. 
        /// </returns>
        public string ToXaml()
        {
            throw new NotImplementedException();

            // var sb = new StringBuilder();
            // var tw = new StringWriter(sb);
            // XmlWriter xw = XmlWriter.Create(tw, new XmlWriterSettings { Indent = true });
            // if (this.canvas != null)
            // {
            // XamlWriter.Save(this.canvas, xw);
            // }

            // xw.Close();
            // return sb.ToString();
        }

        /// <summary>
        /// Zooms the specified axis to the specified values.
        /// </summary>
        /// <param name="axis">
        /// The axis. 
        /// </param>
        /// <param name="p1">
        /// The new minimum value. 
        /// </param>
        /// <param name="p2">
        /// The new maximum value. 
        /// </param>
        public void Zoom(Axis axis, double p1, double p2)
        {
            axis.Zoom(p1, p2);
            this.RefreshPlot(false);
        }

        /// <summary>
        /// Zooms all axes.
        /// </summary>
        /// <param name="delta">
        /// The delta. 
        /// </param>
        public void ZoomAllAxes(double delta)
        {
            foreach (var a in this.ActualModel.Axes)
            {
                this.ZoomAt(a, delta);
            }

            this.RefreshPlot(false);
        }

        /// <summary>
        /// Zooms at the specified position.
        /// </summary>
        /// <param name="axis">
        /// The axis. 
        /// </param>
        /// <param name="factor">
        /// The zoom factor. 
        /// </param>
        /// <param name="x">
        /// The position to zoom at. 
        /// </param>
        public void ZoomAt(Axis axis, double factor, double x = double.NaN)
        {
            if (double.IsNaN(x))
            {
                double sx = (axis.Transform(axis.ActualMaximum) + axis.Transform(axis.ActualMinimum)) * 0.5;
                x = axis.InverseTransform(sx);
            }

            axis.ZoomAt(factor, x);
        }

        #endregion

        #region Methods

        /// <summary>
        /// Called before the <see cref="E:System.Windows.UIElement.KeyDown"/> event occurs.
        /// </summary>
        /// <param name="e">
        /// The data for the event. 
        /// </param>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            bool control = IsControlDown();
            bool alt = IsAltDown();

            switch (e.Key)
            {
                case Key.Home:
                case Key.A:
                    this.ResetAllAxes();
                    e.Handled = true;
                    break;
            }

            double dx = 0;
            double dy = 0;
            double zoom = 0;
            switch (e.Key)
            {
                case Key.Up:
                    dy = -1;
                    break;
                case Key.Down:
                    dy = 1;
                    break;
                case Key.Left:
                    dx = -1;
                    break;
                case Key.Right:
                    dx = 1;
                    break;
                case Key.Add:
                case Key.PageUp:
                    zoom = 1;
                    break;
                case Key.Subtract:
                case Key.PageDown:
                    zoom = -1;
                    break;
            }

            if (!dx.IsZero() || !dy.IsZero())
            {
                dx = dx * this.ActualModel.PlotArea.Width * this.KeyboardPanHorizontalStep;
                dy = dy * this.ActualModel.PlotArea.Height * this.KeyboardPanVerticalStep;

                // small steps if the user is pressing control
                if (control)
                {
                    dx *= 0.2;
                    dy *= 0.2;
                }

                this.PanAll(dx, dy);
                e.Handled = true;
            }

            if (!zoom.IsZero())
            {
                if (control)
                {
                    zoom *= 0.2;
                }

                this.ZoomAllAxes(1 + zoom * 0.12);
                e.Handled = true;
            }

            if (e.Key == Key.C && control)
            {
                // e.Handled = true;
            }

            if (control && alt && this.ActualModel != null)
            {
                switch (e.Key)
                {
                    case Key.R:
                        TrySetClipboardText(this.ActualModel.CreateTextReport());
                        break;
                    case Key.C:
                        TrySetClipboardText(this.ActualModel.ToCode());
                        break;
                    case Key.X:
                        TrySetClipboardText(this.ToXaml());
                        break;
                }
            }

            base.OnKeyDown(e);
        }

        /// <summary>
        /// Called when the <see cref="E:System.Windows.UIElement.ManipulationCompleted"/> event occurs.
        /// </summary>
        /// <param name="e">
        /// The data for the event. 
        /// </param>
        protected override void OnManipulationCompleted(ManipulationCompletedEventArgs e)
        {
            base.OnManipulationCompleted(e);
            var position = this.Add(this.touchDownPoint, e.TotalManipulation.Translation);
            this.touchPan.Completed(new ManipulationEventArgs(position.ToScreenPoint()));
            e.Handled = true;
        }

        /// <summary>
        /// Called when the <see cref="E:System.Windows.UIElement.ManipulationDelta"/> event occurs.
        /// </summary>
        /// <param name="e">
        /// The data for the event. 
        /// </param>
        protected override void OnManipulationDelta(ManipulationDeltaEventArgs e)
        {
            base.OnManipulationDelta(e);
            var position = this.Add(this.touchDownPoint, e.CumulativeManipulation.Translation);
            this.touchPan.Delta(new ManipulationEventArgs(position.ToScreenPoint()));
            this.touchZoom.Delta(
                new ManipulationEventArgs(position.ToScreenPoint())
                    {
                        ScaleX = e.DeltaManipulation.Scale.X,
                        ScaleY = e.DeltaManipulation.Scale.Y
                    });
            e.Handled = true;
        }

        /// <summary>
        /// Called when the <see cref="E:System.Windows.UIElement.ManipulationStarted"/> event occurs.
        /// </summary>
        /// <param name="e">
        /// The data for the event. 
        /// </param>
        protected override void OnManipulationStarted(ManipulationStartedEventArgs e)
        {
            base.OnManipulationStarted(e);
            this.touchPan = new PanManipulator(this);
            this.touchPan.Started(new ManipulationEventArgs(e.ManipulationOrigin.ToScreenPoint()));
            this.touchZoom = new ZoomManipulator(this);
            this.touchZoom.Started(new ManipulationEventArgs(e.ManipulationOrigin.ToScreenPoint()));
            this.touchDownPoint = e.ManipulationOrigin;
            e.Handled = true;
        }

        /// <summary>
        /// Raises the <see cref="E:MouseButtonUp"/> event.
        /// </summary>
        /// <param name="e">
        /// The <see cref="System.Windows.Input.MouseButtonEventArgs"/> instance containing the event data. 
        /// </param>
        protected void OnMouseButtonUp(MouseButtonEventArgs e)
        {
            if (this.mouseManipulator != null)
            {
                this.mouseManipulator.Completed(this.CreateManipulationEventArgs(e));
                e.Handled = true;
            }

            this.mouseManipulator = null;
            this.ReleaseMouseCapture();
        }

        /// <summary>
        /// Called before the <see cref="E:System.Windows.UIElement.MouseLeftButtonDown"/> event occurs.
        /// </summary>
        /// <param name="e">
        /// The data for the event. 
        /// </param>
        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            this.OnMouseButtonDown(MouseButton.Left, e);
            e.Handled = true;
        }

        /// <summary>
        /// Called before the <see cref="E:System.Windows.UIElement.MouseLeftButtonUp"/> event occurs.
        /// </summary>
        /// <param name="e">
        /// The data for the event. 
        /// </param>
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            this.OnMouseButtonUp(e);
            e.Handled = true;
        }

        /// <summary>
        /// Called before the <see cref="E:System.Windows.UIElement.MouseMove"/> event occurs.
        /// </summary>
        /// <param name="e">
        /// The data for the event. 
        /// </param>
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (this.mouseManipulator != null)
            {
                this.mouseManipulator.Delta(this.CreateManipulationEventArgs(e));
            }
        }

        /// <summary>
        /// Raises the <see cref="E:System.Windows.UIElement.MouseRightButtonDown"/> event.
        /// </summary>
        /// <param name="e">
        /// A <see cref="T:System.Windows.Input.MouseButtonEventArgs"/> that contains the event data. 
        /// </param>
        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonDown(e);
            this.OnMouseButtonDown(MouseButton.Right, e);
            if (this.HandleRightClicks)
            {
                e.Handled = true;
            }
        }

        /// <summary>
        /// Raises the <see cref="E:System.Windows.UIElement.MouseRightButtonUp"/> event.
        /// </summary>
        /// <param name="e">
        /// A <see cref="T:System.Windows.Input.MouseButtonEventArgs"/> that contains the event data. 
        /// </param>
        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonUp(e);
            this.OnMouseButtonUp(e);
            if (this.HandleRightClicks)
            {
                e.Handled = true;
            }
        }

        /// <summary>
        /// Called before the <see cref="E:System.Windows.UIElement.MouseWheel"/> event occurs to provide handling for the event in a derived class without attaching a delegate.
        /// </summary>
        /// <param name="e">
        /// A <see cref="T:System.Windows.Input.MouseWheelEventArgs"/> that contains the event data. 
        /// </param>
        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);

            if (!this.IsMouseWheelEnabled)
            {
                return;
            }

            bool isControlDown = IsControlDown();

            var m = new ZoomStepManipulator(this, e.Delta * 0.001, isControlDown);
            m.Started(new ManipulationEventArgs(e.GetPosition(this).ToScreenPoint()));
        }

        /// <summary>
        /// Called when the DataContext is changed.
        /// </summary>
        /// <param name="sender">
        /// The sender. 
        /// </param>
        /// <param name="e">
        /// The <see cref="System.Windows.DependencyPropertyChangedEventArgs"/> instance containing the event data. 
        /// </param>
        private static void DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            ((Plot)sender).OnDataContextChanged(sender, e);
        }

        /// <summary>
        /// Determines whether the alt key is down.
        /// </summary>
        /// <returns>
        /// <c>true</c> if alt is down; otherwise, <c>false</c> . 
        /// </returns>
        private static bool IsAltDown()
        {
            ModifierKeys keys = Keyboard.Modifiers;
            return (keys & ModifierKeys.Alt) != 0;
        }

        /// <summary>
        /// Determines whether the control key is down.
        /// </summary>
        /// <returns>
        /// <c>true</c> if control is down; otherwise, <c>false</c> . 
        /// </returns>
        private static bool IsControlDown()
        {
            ModifierKeys keys = Keyboard.Modifiers;
            return (keys & ModifierKeys.Control) != 0;
        }

        /// <summary>
        /// Determines whether the shift key is down.
        /// </summary>
        /// <returns>
        /// <c>true</c> if shift is down; otherwise, <c>false</c> . 
        /// </returns>
        private static bool IsShiftDown()
        {
            ModifierKeys keys = Keyboard.Modifiers;
            return (keys & ModifierKeys.Shift) != 0;
        }

        /// <summary>
        /// Called when the Model is changed.
        /// </summary>
        /// <param name="d">
        /// The d. 
        /// </param>
        /// <param name="e">
        /// The <see cref="System.Windows.DependencyPropertyChangedEventArgs"/> instance containing the event data. 
        /// </param>
        private static void ModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((Plot)d).OnModelChanged();
        }

        /// <summary>
        /// Sets the clipboard text.
        /// </summary>
        /// <param name="text">
        /// The text. 
        /// </param>
        /// <returns>
        /// True if the operation was successful. 
        /// </returns>
        private static bool TrySetClipboardText(string text)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }

            return false;
        }

        /// <summary>
        /// Adds the coordinates of two points.
        /// </summary>
        /// <param name="p1">
        /// The first point. 
        /// </param>
        /// <param name="p2">
        /// The second point. 
        /// </param>
        /// <returns>
        /// The sum point. 
        /// </returns>
        private Point Add(Point p1, Point p2)
        {
            return new Point(p1.X + p2.X, p1.Y + p2.Y);
        }

        /// <summary>
        /// Called when the composition target rendering event occurs.
        /// </summary>
        /// <param name="sender">
        /// The sender. 
        /// </param>
        /// <param name="e">
        /// The event arguments. 
        /// </param>
        private void CompositionTargetRendering(object sender, EventArgs e)
        {
            lock (this.renderingLock)
            {
                if (this.isPlotInvalidated)
                {
                    this.isPlotInvalidated = false;
                    if (this.ActualWidth > 0 && this.ActualHeight > 0)
                    {
                        this.UpdateModelAndVisuals(this.invalidateUpdatesData);
                        this.invalidateUpdatesData = false;
                    }
                }
            }
        }

        /// <summary>
        /// Creates the manipulation event args.
        /// </summary>
        /// <param name="e">
        /// The <see cref="System.Windows.Input.MouseEventArgs"/> instance containing the event data. 
        /// </param>
        /// <returns>
        /// A manipulation event args object. 
        /// </returns>
        private ManipulationEventArgs CreateManipulationEventArgs(MouseEventArgs e)
        {
            return new ManipulationEventArgs(e.GetPosition(this).ToScreenPoint());
        }

        /// <summary>
        /// Gets the manipulator for the current mouse button and modifier keys.
        /// </summary>
        /// <param name="button">
        /// The button. 
        /// </param>
        /// <param name="clickCount">
        /// The click count. 
        /// </param>
        /// <param name="e">
        /// The event args. 
        /// </param>
        /// <returns>
        /// A manipulator or null if no gesture was recognized. 
        /// </returns>
        private ManipulatorBase GetManipulator(MouseButton button, int clickCount, MouseButtonEventArgs e)
        {
            bool control = IsControlDown();
            bool shift = IsShiftDown();
            bool alt = IsAltDown();
            bool lmb = button == MouseButton.Left;
            bool rmb = button == MouseButton.Right;
            bool mmb = button == MouseButton.Middle;
            bool xb1 = button == MouseButton.XButton1;
            bool xb2 = button == MouseButton.XButton2;

            // MMB / control RMB / control+alt LMB
            if (mmb || (control && rmb) || (control && alt && lmb))
            {
                if (clickCount == 2)
                {
                    return new ResetManipulator(this);
                }

                return new ZoomRectangleManipulator(this);
            }

            // Right mouse button / alt+left mouse button
            if (rmb || (lmb && alt))
            {
                return new PanManipulator(this);
            }

            // Left mouse button
            if (lmb)
            {
                return new TrackerManipulator(this) { Snap = !control, PointsOnly = shift };
            }

            // XButtons are zoom-stepping
            if (xb1 || xb2)
            {
                double d = xb1 ? 0.05 : -0.05;
                return new ZoomStepManipulator(this, d, control);
            }

            return null;
        }

        /// <summary>
        /// Called when data context is changed.
        /// </summary>
        /// <param name="sender">
        /// The sender. 
        /// </param>
        /// <param name="e">
        /// The <see cref="System.Windows.DependencyPropertyChangedEventArgs"/> instance containing the event data. 
        /// </param>
        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            this.OnModelChanged();
        }

        /// <summary>
        /// Called when the control is loaded.
        /// </summary>
        /// <param name="sender">
        /// The sender. 
        /// </param>
        /// <param name="e">
        /// The <see cref="System.Windows.RoutedEventArgs"/> instance containing the event data. 
        /// </param>
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            this.OnModelChanged();
        }

        /// <summary>
        /// Called when the model is changed.
        /// </summary>
        private void OnModelChanged()
        {
            lock (this.modelLock)
            {
                if (this.currentModel != null)
                {
                    this.currentModel.PlotControl = null;
                }

                if (this.Model != null)
                {
                    if (this.Model.PlotControl != null)
                    {
                        throw new InvalidOperationException(
                            "This PlotModel is already in use by some other plot control.");
                    }

                    this.Model.PlotControl = this;
                    this.currentModel = this.Model;
                }
            }

            this.InvalidatePlot();
        }

        /// <summary>
        /// Called when a mouse button is pressed down.
        /// </summary>
        /// <param name="button">
        /// The button. 
        /// </param>
        /// <param name="e">
        /// The <see cref="System.Windows.Input.MouseButtonEventArgs"/> instance containing the event data. 
        /// </param>
        private void OnMouseButtonDown(MouseButton button, MouseButtonEventArgs e)
        {
            if (this.mouseManipulator != null)
            {
                return;
            }

            this.Focus();
            this.CaptureMouse();

            int clickCount = 1;
            if (MouseButtonHelper.IsDoubleClick(this, e))
            {
                clickCount = 2;
            }

            this.mouseManipulator = this.GetManipulator(button, clickCount, e);

            if (this.mouseManipulator != null)
            {
                this.mouseManipulator.Started(this.CreateManipulationEventArgs(e));
            }
        }

        /// <summary>
        /// Called when the size of the control is changed.
        /// </summary>
        /// <param name="sender">
        /// The sender. 
        /// </param>
        /// <param name="e">
        /// The <see cref="System.Windows.SizeChangedEventArgs"/> instance containing the event data. 
        /// </param>
        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            this.InvalidatePlot(false);
        }

        /// <summary>
        /// Pans all axes.
        /// </summary>
        /// <param name="dx">
        /// The dx. 
        /// </param>
        /// <param name="dy">
        /// The dy. 
        /// </param>
        private void PanAll(double dx, double dy)
        {
            foreach (var a in this.ActualModel.Axes)
            {
                a.Pan(a.IsHorizontal() ? dx : dy);
            }

            this.RefreshPlot(false);
        }

        /// <summary>
        /// Synchronize properties between the Silverlight control and the internal PlotModel (only if Model is undefined).
        /// </summary>
        private void SynchronizeProperties()
        {
        }

        /// <summary>
        /// Updates the model.
        /// </summary>
        /// <param name="updateData">
        /// The update Data. 
        /// </param>
        private void UpdateModel(bool updateData = true)
        {
            this.internalModel = this.Model;

            if (this.ActualModel != null)
            {
                this.ActualModel.Update(updateData);
            }
        }

        /// <summary>
        /// Updates the model. If Model==null, an internal model will be created. The ActualModel.UpdateModel will be called (updates all series data).
        /// </summary>
        /// <param name="updateData">
        /// if set to <c>true</c> , all data collections will be updated. 
        /// </param>
        private void UpdateModelAndVisuals(bool updateData = true)
        {
            lock (this.updateModelAndVisualsLock)
            {
                this.UpdateModel(updateData);
                this.UpdateVisuals();
            }
        }

        /// <summary>
        /// Updates the visuals.
        /// </summary>
        private void UpdateVisuals()
        {
            if (this.canvas == null)
            {
                return;
            }

            // Clear the canvas
            this.canvas.Children.Clear();

            if (this.ActualModel != null)
            {
                this.SynchronizeProperties();

                var wrc = new SilverlightRenderContext(this.canvas);
                this.ActualModel.Render(wrc);
            }
        }

        #endregion
    }
}