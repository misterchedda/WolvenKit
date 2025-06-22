using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ReactiveUI;
using Splat;
using WolvenKit.App.ViewModels.GraphEditor;
using WolvenKit.App.ViewModels.GraphEditor.Nodes.Quest;
using WolvenKit.App.ViewModels.GraphEditor.Nodes.Scene;
using WolvenKit.Core.Interfaces;
using WolvenKit.RED4.Types;
using WolvenKit.Views.GraphEditor;
using WolvenKit.Views.Tools;

namespace WolvenKit.Views.Documents;
/// <summary>
/// Interaktionslogik für RDTGraphView2.xaml
/// </summary>
public partial class RDTGraphView2
{
    private bool _isFullScreen = false;
    private System.Windows.Window _fullScreenWindow = null;
    private WolvenKit.App.ViewModels.Documents.RDTDataViewModel _fullScreenDataViewModel;
    private WolvenKit.App.ViewModels.Documents.RDTGraphViewModel2 _fullScreenGraphViewModel;
    private System.Windows.Threading.DispatcherTimer _graphRefreshTimer;
    private bool _graphRefreshPending = false;
    private System.Windows.Controls.StackPanel _fullScreenBreadcrumb;

    public RDTGraphView2()
    {
        InitializeComponent();

        KeyDown += OnKeyDown;

        this.WhenActivated(disposables =>
        {
            BuildBreadcrumb();
        });
    }

    private void Editor_OnNodeDoubleClick(object sender, RoutedEventArgs e) => HandleSubGraph();

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab)
        {
            HandleSubGraph();
        }
    }

    private void HandleSubGraph()
    {
        if (Editor.SelectedNode is IGraphProvider provider)
        {
            var subGraph = provider.Graph;
            if (subGraph == null)
            {
                Locator.Current.GetService<ILoggerService>().Error("SubGraph is not defined!");
                return;
            }

            if (Editor.SelectedNode.Data is questPhaseNodeDefinition ph)
            {
                if (!ph.PhaseResource.IsSet)
                {
                    subGraph.StateParents = Editor.Source.StateParents + "." + ph.Id;
                    subGraph.DocumentViewModel = Editor.Source.DocumentViewModel;
                }
            }

            ViewModel!.History.Add(subGraph);
            Editor.SetCurrentValue(GraphEditorView.SourceProperty, subGraph);

            BuildBreadcrumb();
        }

        if (Editor.SelectedNode is questInputNodeDefinitionWrapper or questOutputNodeDefinitionWrapper)
        {
            if (ViewModel!.History.Count > 1)
            {
                ViewModel.History.Remove(ViewModel.History[^1]);
                Editor.SetCurrentValue(GraphEditorView.SourceProperty, ViewModel.History[^1]);

                BuildBreadcrumb();
            }
        }

        if (Editor.SelectedNode is scnStartNodeWrapper or scnEndNodeWrapper)
        {
            if (ViewModel!.History.Count > 1)
            {
                ViewModel.History.Remove(ViewModel.History[^1]);
                Editor.SetCurrentValue(GraphEditorView.SourceProperty, ViewModel.History[^1]);

                BuildBreadcrumb();
            }
        }
    }

    private void BuildBreadcrumb()
    {
        Breadcrumb.Children.Clear();

        // Add filename with dirty indicator at the beginning (only in fullscreen mode)
        if (_isFullScreen && ViewModel?.Parent is WolvenKit.App.ViewModels.Documents.RedDocumentViewModel docViewModel)
        {
            var filename = System.IO.Path.GetFileName(docViewModel.FilePath);
            var dirtyIndicator = docViewModel.IsDirty ? "*" : "";
            
            var fileElement = new TextBlock 
            { 
                Text = $"{filename}{dirtyIndicator}",
                FontWeight = System.Windows.FontWeights.Bold,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White),
                Margin = new System.Windows.Thickness(0, 0, 10, 0)
            };
            Breadcrumb.Children.Add(fileElement);
            
            // Add separator after filename if there are graph items
            if (ViewModel!.History.Count > 0)
            {
                AddNewElement("|", null);
            }
        }

        for (var i = 0; i < ViewModel!.History.Count; i++)
        {
            AddNewElement(ViewModel.History[i].Title, ViewModel.History[i]);

            if (i < ViewModel.History.Count - 1)
            {
                AddNewElement(">", null);
            }
        }

        void AddNewElement(string text, RedGraph graph)
        {
            if (Breadcrumb.Children.Count > 0)
            {
                text = " " + text;
            }

            var tmp = new TextBlock { Text = text, Tag = graph };
            tmp.PreviewMouseDown += BreadcrumbElement_OnPreviewMouseDown;

            Breadcrumb.Children.Add(tmp);
        }
    }

    private void BreadcrumbElement_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBlock { Tag: RedGraph graph } block)
        {
            return;
        }

        if (ViewModel!.History.Count == 1)
        {
            return;
        }

        if (block.Text.Trim() == ">")
        {
            return;
        }

        Editor.SetCurrentValue(GraphEditorView.SourceProperty, graph);

        for (var i = ViewModel.History.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(ViewModel.History[i], graph))
            {
                break;
            }
            ViewModel.History.RemoveAt(i);
        }

        BuildBreadcrumb();
    }

    private void FullScreenButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleFullScreen();
    }

    private void ToggleFullScreen()
    {
        if (!_isFullScreen)
        {
            EnterFullScreen();
        }
        else
        {
            ExitFullScreen();
        }
    }

    private void EnterFullScreen()
    {
        // Create a new full screen window
        _fullScreenWindow = new System.Windows.Window
        {
            WindowStyle = WindowStyle.None,
            WindowState = WindowState.Maximized,
            AllowsTransparency = false,
            Background = System.Windows.Media.Brushes.Black
        };

        // Check if this is a quest or scene file that can benefit from combined view
        if (ViewModel?.Parent is WolvenKit.App.ViewModels.Documents.RedDocumentViewModel docViewModel)
        {
            // Set initial window title with filename and dirty indicator
            UpdateFullScreenWindowTitle(docViewModel);
            
            // Subscribe to property changes to update title when dirty state changes
            docViewModel.PropertyChanged += OnDocumentPropertyChanged;

            // Get the existing tab data instead of creating new view models
            var dataTab = docViewModel.TabItemViewModels.OfType<WolvenKit.App.ViewModels.Documents.RDTDataViewModel>().FirstOrDefault();
            var graphTab = docViewModel.TabItemViewModels.OfType<WolvenKit.App.ViewModels.Documents.RDTGraphViewModel2>().FirstOrDefault();
            
            if (dataTab != null && graphTab != null)
            {
                var data = dataTab.GetData();
                if (data is graphGraphResource or scnSceneResource)
                {
                    // Use existing loaded view models to avoid initialization issues
                    var fullScreenCombinedView = CreateFullScreenCombinedViewFromExisting(dataTab, graphTab);
                    _fullScreenWindow.SetCurrentValue(System.Windows.Window.ContentProperty, fullScreenCombinedView);
                }
                else
                {
                    // Not a quest/scene file, use regular fullscreen
                    CreateRegularFullScreenView();
                }
            }
            else
            {
                // Fallback to regular graph editor
                CreateRegularFullScreenView();
            }

            docViewModel.OnSaveCompleted += OnSaveCompleted;
        }
        else
        {
            // Regular graph editor fullscreen
            CreateRegularFullScreenView();
        }
        
        // Add escape key handler
        _fullScreenWindow.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape || e.Key == Key.F11)
            {
                ExitFullScreen();
            }

            if (e.Key == Key.S && (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl)))
            {
                ViewModel?.Parent?.Save(null);
                e.Handled = true; // prevent further handling
            }
        };
        
        _fullScreenWindow.Show();

        // Update button state
        _isFullScreen = true;
        UpdateFullScreenButtonIcon();

        // Handle window closing
        _fullScreenWindow.Closed += (s, e) => ExitFullScreen();
    }

    private void ExitFullScreen()
    {
        if (_fullScreenWindow == null) return;

        // Stop and cleanup the refresh timer
        if (_graphRefreshTimer != null)
        {
            _graphRefreshTimer.Stop();
            _graphRefreshTimer = null;
        }

        if (ViewModel?.Parent is WolvenKit.App.ViewModels.Documents.RedDocumentViewModel docViewModel)
        {
            docViewModel.OnSaveCompleted -= OnSaveCompleted;
            docViewModel.PropertyChanged -= OnDocumentPropertyChanged;
            docViewModel.PropertyChanged -= OnFullScreenDocumentPropertyChanged;
        }

        // Close and cleanup full screen window
        _fullScreenWindow.Close();
        _fullScreenWindow = null;
        
        // Clear references
        _fullScreenDataViewModel = null;
        _fullScreenGraphViewModel = null;
        _fullScreenBreadcrumb = null;
        _graphRefreshPending = false;
        
        // Update state
        _isFullScreen = false;
        UpdateFullScreenButtonIcon();
    }

    private void UpdateFullScreenButtonIcon()
    {
        if (FullScreenButton?.Content is Viewbox viewbox &&
            viewbox.Child is Canvas canvas &&
            canvas.Children[0] is System.Windows.Shapes.Path path)
        {
            if (_isFullScreen)
            {
                // Exit full screen icon
                path.Data = System.Windows.Media.Geometry.Parse("M5,16h3v3h2v-5H5V16z M8,8H5v2h5V5H8V8z M14,19h2v-3h3v-2h-5V19z M16,8V5h-2v5h5V8H16z");
            }
            else
            {
                // Enter full screen icon
                path.Data = System.Windows.Media.Geometry.Parse("M7,14H5v5h5v-2H7V14z M5,10h2V7h3V5H5V10z M17,7h-3v2h3v3h2V7V5h-2V7z M14,14h3v3h2v-5h-5V14z");
            }
        }
    }

    private void CreateRegularFullScreenView()
    {
        // Create a new GraphEditorView for the full screen window
        var fullScreenEditor = new GraphEditorView
        {
            Source = Editor.Source,
            SelectedNode = Editor.SelectedNode,
            SelectedNodes = Editor.SelectedNodes,
            ViewportLocation = Editor.ViewportLocation
        };
        
        _fullScreenWindow.SetCurrentValue(System.Windows.Window.ContentProperty, fullScreenEditor);
    }

    private System.Windows.UIElement CreateFullScreenCombinedViewFromExisting(
        WolvenKit.App.ViewModels.Documents.RDTDataViewModel dataViewModel,
        WolvenKit.App.ViewModels.Documents.RDTGraphViewModel2 graphViewModel)
    {
        // Create a top-level Grid with breadcrumb at the top
        var topLevelGrid = new System.Windows.Controls.Grid();
        topLevelGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto }); // Breadcrumb row
        topLevelGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) }); // Content row

        // Create breadcrumb panel at the very top
        var breadcrumbPanel = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(48, 48, 48)),
            Height = 30,
            Margin = new System.Windows.Thickness(0, 0, 0, 2)
        };
        
        // Add filename with dirty indicator
        if (graphViewModel.Parent is WolvenKit.App.ViewModels.Documents.RedDocumentViewModel parentDoc)
        {
            var filename = System.IO.Path.GetFileName(parentDoc.FilePath);
            var dirtyIndicator = parentDoc.IsDirty ? "*" : "";
            
            var fileElement = new System.Windows.Controls.TextBlock 
            { 
                Text = $"{filename}{dirtyIndicator}",
                FontWeight = System.Windows.FontWeights.Bold,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White),
                Margin = new System.Windows.Thickness(10, 0, 10, 0),
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            breadcrumbPanel.Children.Add(fileElement);
            
            // Add separator
            var separator = new System.Windows.Controls.TextBlock 
            { 
                Text = "|",
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray),
                Margin = new System.Windows.Thickness(0, 0, 10, 0),
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            breadcrumbPanel.Children.Add(separator);
            
            // Store reference to breadcrumb for updates
            _fullScreenBreadcrumb = breadcrumbPanel;
            
            // Subscribe to property changes to update breadcrumb
            parentDoc.PropertyChanged += OnFullScreenDocumentPropertyChanged;
        }
        
        // Add the graph title
        var graphTitle = new System.Windows.Controls.TextBlock 
        { 
            Text = graphViewModel.MainGraph?.Title ?? "Graph",
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White),
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
        breadcrumbPanel.Children.Add(graphTitle);
        
        System.Windows.Controls.Grid.SetRow(breadcrumbPanel, 0);
        topLevelGrid.Children.Add(breadcrumbPanel);

        // Create the main content grid for the combined fullscreen layout
        var mainGrid = new System.Windows.Controls.Grid();
        
        // Define columns: Tree (30%) | Graph (50%) with splitter
        // Note: The tree panel internally has its own columns for tree (60%) and properties (40%)
        // So the effective widths will be: Tree (18%) | Properties (12%) | Graph (50%)
        // To get Tree 30% and Properties 20%, we need to adjust the main columns
        mainGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(50, System.Windows.GridUnitType.Star) }); // This will contain tree (30%) + properties (20%)
        mainGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(4, System.Windows.GridUnitType.Pixel) });
        mainGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(50, System.Windows.GridUnitType.Star) }); // Graph (50%)

        // Tree View Panel
        var treePanel = CreateFullScreenTreePanel("Data Structure", dataViewModel);
        System.Windows.Controls.Grid.SetColumn(treePanel, 0);
        mainGrid.Children.Add(treePanel);

        // Store references for synchronization
        _fullScreenDataViewModel = dataViewModel;
        _fullScreenGraphViewModel = graphViewModel;

        // Splitter
        var splitter1 = new System.Windows.Controls.GridSplitter
        {
            Width = 4,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(64, 64, 64))
        };
        System.Windows.Controls.Grid.SetColumn(splitter1, 1);
        mainGrid.Children.Add(splitter1);

        // Graph View Panel - Use the existing graph editor directly
        var graphPanel = CreateGraphPanelWithExistingEditor(graphViewModel);
        System.Windows.Controls.Grid.SetColumn(graphPanel, 2);
        mainGrid.Children.Add(graphPanel);

        // Add main grid to the content row
        System.Windows.Controls.Grid.SetRow(mainGrid, 1);
        topLevelGrid.Children.Add(mainGrid);

        return topLevelGrid;
    }

    private System.Windows.Controls.Border CreateFullScreenTreePanel(string title, WolvenKit.App.ViewModels.Documents.RDTDataViewModel treeViewModel)
    {
        var border = new System.Windows.Controls.Border
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(32, 32, 32)),
            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(64, 64, 64)),
            BorderThickness = new System.Windows.Thickness(1, 1, 1, 1)
        };

        var mainGrid = new System.Windows.Controls.Grid();
        mainGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
        mainGrid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });

        // Header
        var header = new System.Windows.Controls.Border
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(48, 48, 48)),
            Padding = new System.Windows.Thickness(8, 4, 8, 4)
        };
        var headerText = new System.Windows.Controls.TextBlock
        {
            Text = title,
            FontWeight = System.Windows.FontWeights.Bold,
            FontSize = 14,
            Foreground = System.Windows.Media.Brushes.White
        };
        header.Child = headerText;
        System.Windows.Controls.Grid.SetRow(header, 0);
        mainGrid.Children.Add(header);

        // Content Grid - Contains tree and property editor side by side
        var contentGrid = new System.Windows.Controls.Grid();
        contentGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(3, System.Windows.GridUnitType.Star) }); // Tree 30% (3/5 of 50%)
        contentGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(4, System.Windows.GridUnitType.Pixel) });
        contentGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(2, System.Windows.GridUnitType.Star) }); // Properties 20% (2/5 of 50%)

        // Tree View (left side)
        var treeView = new WolvenKit.Views.Tools.RedTreeView
        {
            ItemsSource = treeViewModel.Chunks,
            SelectedItem = treeViewModel.SelectedChunk,
            Margin = new System.Windows.Thickness(4, 4, 4, 4)
        };
        
        // Set up two-way binding for selected item
        var selectedItemBinding = new System.Windows.Data.Binding("SelectedChunk")
        {
            Source = treeViewModel,
            Mode = System.Windows.Data.BindingMode.TwoWay
        };
        treeView.SetBinding(WolvenKit.Views.Tools.RedTreeView.SelectedItemProperty, selectedItemBinding);
        
        // Set up two-way binding for selected items
        var selectedItemsBinding = new System.Windows.Data.Binding("SelectedChunks")
        {
            Source = treeViewModel,
            Mode = System.Windows.Data.BindingMode.TwoWay
        };
        treeView.SetBinding(WolvenKit.Views.Tools.RedTreeView.SelectedItemsProperty, selectedItemsBinding);
        
        System.Windows.Controls.Grid.SetColumn(treeView, 0);
        contentGrid.Children.Add(treeView);

        // Splitter between tree and properties
        var splitter = new System.Windows.Controls.GridSplitter
        {
            Width = 4,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(64, 64, 64))
        };
        System.Windows.Controls.Grid.SetColumn(splitter, 1);
        contentGrid.Children.Add(splitter);

        // Property Editor (right side)
        var propertyEditor = new WolvenKit.Views.Editors.RedTypeView();
        
        // Bind its DataContext to the SelectedChunk
        var dataContextBinding = new System.Windows.Data.Binding("SelectedChunk")
        {
            Source = treeViewModel,
            Mode = System.Windows.Data.BindingMode.OneWay
        };
        propertyEditor.SetBinding(System.Windows.Controls.UserControl.DataContextProperty, dataContextBinding);
        
        System.Windows.Controls.Grid.SetColumn(propertyEditor, 2);
        contentGrid.Children.Add(propertyEditor);

        System.Windows.Controls.Grid.SetRow(contentGrid, 1);
        mainGrid.Children.Add(contentGrid);

        border.Child = mainGrid;
        return border;
    }

    private System.Windows.Controls.Border CreateGraphPanelFromExisting(WolvenKit.App.ViewModels.Documents.RDTGraphViewModel2 graphViewModel)
    {
        var border = new System.Windows.Controls.Border
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(32, 32, 32)),
            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(64, 64, 64)),
            BorderThickness = new System.Windows.Thickness(1, 1, 1, 1)
        };

        var grid = new System.Windows.Controls.Grid();
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });

        // Header
        var header = new System.Windows.Controls.Border
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(48, 48, 48)),
            Padding = new System.Windows.Thickness(8, 4, 8, 4)
        };
        var headerText = new System.Windows.Controls.TextBlock
        {
            Text = "Graph View",
            FontWeight = System.Windows.FontWeights.Bold,
            FontSize = 14,
            Foreground = System.Windows.Media.Brushes.White
        };
        header.Child = headerText;
        System.Windows.Controls.Grid.SetRow(header, 0);
        grid.Children.Add(header);

        // Graph content placeholder to avoid crashes
        var graphPlaceholder = new System.Windows.Controls.TextBlock
        {
            Text = "Graph View\n\nNote: Graph rendering in fullscreen mode is temporarily disabled due to initialization issues.\nPlease use the regular Graph tab for full graph functionality.",
            TextWrapping = System.Windows.TextWrapping.Wrap,
            FontSize = 14,
            Foreground = System.Windows.Media.Brushes.Gray,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new System.Windows.Thickness(20)
        };
        
        System.Windows.Controls.Grid.SetRow(graphPlaceholder, 1);
        grid.Children.Add(graphPlaceholder);

        border.Child = grid;
        return border;
    }

    private System.Windows.Controls.Border CreateGraphPanelWithExistingEditor(WolvenKit.App.ViewModels.Documents.RDTGraphViewModel2 graphViewModel)
    {
        var border = new System.Windows.Controls.Border
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(32, 32, 32)),
            BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(64, 64, 64)),
            BorderThickness = new System.Windows.Thickness(1, 1, 1, 1)
        };

        var grid = new System.Windows.Controls.Grid();
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });

        // Header
        var header = new System.Windows.Controls.Border
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(48, 48, 48)),
            Padding = new System.Windows.Thickness(8, 4, 8, 4)
        };
        var headerText = new System.Windows.Controls.TextBlock
        {
            Text = "Graph View",
            FontWeight = System.Windows.FontWeights.Bold,
            FontSize = 14,
            Foreground = System.Windows.Media.Brushes.White
        };
        header.Child = headerText;
        System.Windows.Controls.Grid.SetRow(header, 0);
        grid.Children.Add(header);

        // Create a simple wrapper to hold the graph without triggering automatic layout
        var graphWrapper = new System.Windows.Controls.Border
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(32, 32, 32))
        };
        
        // Create a loading indicator
        var loadingPanel = new System.Windows.Controls.Grid();
        
        var progressRing = new HandyControl.Controls.LoadingCircle
        {
            Width = 60,
            Height = 60,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 149, 237)) // Cornflower blue
        };
        
        var loadingText = new System.Windows.Controls.TextBlock
        {
            Text = "Loading graph...",
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Foreground = System.Windows.Media.Brushes.Gray,
            FontSize = 14,
            Margin = new System.Windows.Thickness(0, 80, 0, 0)
        };
        
        loadingPanel.Children.Add(progressRing);
        loadingPanel.Children.Add(loadingText);
        graphWrapper.Child = loadingPanel;
        
        // Defer creating the actual graph view to avoid initialization issues
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            new Action(() =>
            {
                try
                {
                    // Create the graph editor view directly without RDTGraphView2 wrapper
                    var graphEditorView = new GraphEditorView
                    {
                        Margin = new System.Windows.Thickness(4, 4, 4, 4)
                    };
                    
                    // Bind the Source property to the MainGraph on the viewmodel
                    var sourceBinding = new System.Windows.Data.Binding("MainGraph")
                    {
                        Source = graphViewModel,
                        Mode = System.Windows.Data.BindingMode.OneWay
                    };
                    graphEditorView.SetBinding(GraphEditorView.SourceProperty, sourceBinding);

                    // Replace the loading text with the actual graph
                    graphWrapper.Child = graphEditorView;
                    
                    // Set up synchronization after the graph is loaded
                    if (_fullScreenDataViewModel != null && _fullScreenGraphViewModel != null)
                    {
                        SetupTreeGraphSync(_fullScreenDataViewModel, _fullScreenGraphViewModel, graphEditorView);
                    }
                }
                catch (Exception ex)
                {
                    // If there's still an error, show it
                    graphWrapper.Child = new System.Windows.Controls.TextBlock
                    {
                        Text = $"Error loading graph: {ex.Message}",
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                        VerticalAlignment = System.Windows.VerticalAlignment.Center,
                        Foreground = System.Windows.Media.Brushes.Red,
                        FontSize = 12,
                        TextWrapping = System.Windows.TextWrapping.Wrap,
                        Margin = new System.Windows.Thickness(20)
                    };
                }
            }),
            System.Windows.Threading.DispatcherPriority.Background
        );
        
        System.Windows.Controls.Grid.SetRow(graphWrapper, 1);
        grid.Children.Add(graphWrapper);

        border.Child = grid;
        return border;
    }

    private T FindVisualChild<T>(System.Windows.DependencyObject parent, string name = null) where T : System.Windows.FrameworkElement
    {
        if (parent == null) return null;

        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            
            if (child is T typedChild)
            {
                if (name == null || typedChild.Name == name)
                    return typedChild;
            }

            var result = FindVisualChild<T>(child, name);
            if (result != null)
                return result;
        }

        return null;
    }

    private void SetupTreeGraphSync(
        WolvenKit.App.ViewModels.Documents.RDTDataViewModel dataViewModel,
        WolvenKit.App.ViewModels.Documents.RDTGraphViewModel2 graphViewModel,
        GraphEditorView graphEditor)
    {
        // Subscribe to tree selection changes to update graph
        if (dataViewModel != null)
        {
            dataViewModel.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(dataViewModel.SelectedChunk))
                {
                    // When tree selection changes, try to find and select corresponding graph node
                    var selectedChunk = dataViewModel.SelectedChunk;
                    if (selectedChunk != null && graphViewModel?.MainGraph != null)
                    {
                        // Find corresponding node in graph
                        var correspondingNode = graphViewModel.MainGraph.Nodes?.FirstOrDefault(n => 
                            n.Data == selectedChunk.Data || 
                            (n.Data != null && selectedChunk.Data != null && n.Data.Equals(selectedChunk.Data)));
                        
                        if (correspondingNode != null && graphEditor != null)
                        {
                            // Update graph selection
                            graphEditor.SelectedNode = correspondingNode;
                            
                            // Center the node in view
                            if (correspondingNode.Location != default)
                            {
                                graphEditor.ViewportLocation = new System.Windows.Point(
                                    correspondingNode.Location.X - graphEditor.ActualWidth / 2,
                                    correspondingNode.Location.Y - graphEditor.ActualHeight / 2);
                            }
                        }
                    }
                }
            };

            // Subscribe to data changes in tree to update graph
            SetupTreeDataChangeSync(dataViewModel, graphViewModel, graphEditor);
        }

        // Subscribe to graph selection changes to update tree
        if (graphViewModel?.MainGraph != null && graphEditor != null)
        {
            graphEditor.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(graphEditor.SelectedNode))
                {
                    var selectedNode = graphEditor.SelectedNode;
                    if (selectedNode != null && dataViewModel != null)
                    {
                        // Find corresponding chunk in tree
                        var correspondingChunk = FindChunkInTree(dataViewModel.RootChunk, selectedNode.Data);
                        if (correspondingChunk != null)
                        {
                            dataViewModel.SelectedChunk = correspondingChunk;
                        }
                    }
                }
            };

            // Subscribe to graph data changes to update tree
            SetupGraphDataChangeSync(dataViewModel, graphViewModel);
        }
    }

    private WolvenKit.App.ViewModels.Shell.ChunkViewModel FindChunkInTree(WolvenKit.App.ViewModels.Shell.ChunkViewModel root, object targetData)
    {
        if (root == null || targetData == null) return null;

        // Check if this chunk matches
        if (root.Data == targetData || (root.Data != null && root.Data.Equals(targetData)))
        {
            return root;
        }

        // Recursively search children
        if (root.Properties != null)
        {
            foreach (var property in root.Properties)
            {
                var found = FindChunkInTree(property, targetData);
                if (found != null) return found;
            }
        }

        if (root.TVProperties != null)
        {
            foreach (var property in root.TVProperties)
            {
                var found = FindChunkInTree(property, targetData);
                if (found != null) return found;
            }
        }

        return null;
    }

    private void SetupTreeDataChangeSync(
        WolvenKit.App.ViewModels.Documents.RDTDataViewModel dataViewModel,
        WolvenKit.App.ViewModels.Documents.RDTGraphViewModel2 graphViewModel,
        GraphEditorView graphEditor)
    {
        if (dataViewModel?.RootChunk == null || graphViewModel?.MainGraph == null) return;

        // The old logic for real-time sync via timers is being replaced
        // by the OnSaveCompleted event, which is more reliable.
        // We can leave this method empty for now, or remove the call to it.
    }

    private void SetupGraphDataChangeSync(
        WolvenKit.App.ViewModels.Documents.RDTDataViewModel dataViewModel,
        WolvenKit.App.ViewModels.Documents.RDTGraphViewModel2 graphViewModel)
    {
        if (graphViewModel?.MainGraph?.Nodes == null || dataViewModel == null) return;

        // Subscribe to changes in graph nodes
        foreach (var node in graphViewModel.MainGraph.Nodes)
        {
            if (node is System.ComponentModel.INotifyPropertyChanged notifyNode)
            {
                notifyNode.PropertyChanged += (sender, e) =>
                {
                    // When graph node changes, find corresponding tree item and refresh it
                    var changedNode = sender as WolvenKit.App.ViewModels.GraphEditor.NodeViewModel;
                    if (changedNode?.Data != null)
                    {
                        var correspondingChunk = FindChunkInTree(dataViewModel.RootChunk, changedNode.Data);
                        if (correspondingChunk != null)
                        {
                            // Trigger tree refresh using dispatcher
                            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                            {
                                // Force tree UI refresh by marking as dirty
                                if (!dataViewModel.DirtyChunks.Contains(correspondingChunk))
                                {
                                    dataViewModel.DirtyChunks.Add(correspondingChunk);
                                }
                                dataViewModel.Parent?.SetIsDirty(true);
                            });
                        }
                    }
                };
            }
        }

        // Subscribe to node collection changes (add/remove nodes)
        if (graphViewModel.MainGraph.Nodes is System.Collections.Specialized.INotifyCollectionChanged nodeCollection)
        {
            nodeCollection.CollectionChanged += (sender, e) =>
            {
                // When nodes are added/removed in graph, refresh the entire tree structure
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                {
                    // Force full tree refresh by marking root as dirty
                    if (dataViewModel.RootChunk != null && !dataViewModel.DirtyChunks.Contains(dataViewModel.RootChunk))
                    {
                        dataViewModel.DirtyChunks.Add(dataViewModel.RootChunk);
                    }
                    dataViewModel.Parent?.SetIsDirty(true);
                });
                
                // Re-setup subscriptions for new nodes
                if (e.NewItems != null)
                {
                    foreach (var newNode in e.NewItems.OfType<System.ComponentModel.INotifyPropertyChanged>())
                    {
                        newNode.PropertyChanged += (nodeSender, nodeE) =>
                        {
                            var changedNode = nodeSender as WolvenKit.App.ViewModels.GraphEditor.NodeViewModel;
                            if (changedNode?.Data != null)
                            {
                                var correspondingChunk = FindChunkInTree(dataViewModel.RootChunk, changedNode.Data);
                                if (correspondingChunk != null)
                                {
                                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                                    {
                                        // Force tree UI refresh by marking as dirty
                                        if (!dataViewModel.DirtyChunks.Contains(correspondingChunk))
                                        {
                                            dataViewModel.DirtyChunks.Add(correspondingChunk);
                                        }
                                        dataViewModel.Parent?.SetIsDirty(true);
                                    });
                                }
                            }
                        };
                    }
                }
            };
        }
    }

    private void SubscribeToChunkChanges(WolvenKit.App.ViewModels.Shell.ChunkViewModel chunk, System.Action<WolvenKit.App.ViewModels.Shell.ChunkViewModel> onChunkChanged)
    {
        if (chunk == null) return;

        // Subscribe to this chunk's property changes
        chunk.PropertyChanged += (sender, e) =>
        {
            onChunkChanged(chunk);
        };

        // Subscribe to collection changes for Properties
        if (chunk.Properties is System.Collections.Specialized.INotifyCollectionChanged propertiesCollection)
        {
            propertiesCollection.CollectionChanged += (sender, e) =>
            {
                onChunkChanged(chunk);
                
                // Subscribe to new items
                if (e.NewItems != null)
                {
                    foreach (var newItem in e.NewItems)
                    {
                        if (newItem is WolvenKit.App.ViewModels.Shell.ChunkViewModel newChunk)
                        {
                            SubscribeToChunkChanges(newChunk, onChunkChanged);
                        }
                    }
                }
            };
        }

        // Subscribe to collection changes for TVProperties
        if (chunk.TVProperties is System.Collections.Specialized.INotifyCollectionChanged tvPropertiesCollection)
        {
            tvPropertiesCollection.CollectionChanged += (sender, e) =>
            {
                onChunkChanged(chunk);
                
                // Subscribe to new items
                if (e.NewItems != null)
                {
                    foreach (var newItem in e.NewItems)
                    {
                        if (newItem is WolvenKit.App.ViewModels.Shell.ChunkViewModel newChunk)
                        {
                            SubscribeToChunkChanges(newChunk, onChunkChanged);
                        }
                    }
                }
            };
        }

        // Recursively subscribe to all existing child chunks
        if (chunk.Properties != null)
        {
            foreach (var property in chunk.Properties)
            {
                SubscribeToChunkChanges(property, onChunkChanged);
            }
        }

        if (chunk.TVProperties != null)
        {
            foreach (var property in chunk.TVProperties)
            {
                SubscribeToChunkChanges(property, onChunkChanged);
            }
        }
    }

    private void MonitorSceneGraphChanges(object sceneGraph)
    {
        // Monitor scene graph for changes
        var graphType = sceneGraph.GetType();
        var nodesProperty = graphType.GetProperty("Nodes");
        if (nodesProperty != null)
        {
            var nodes = nodesProperty.GetValue(sceneGraph);
            if (nodes is System.Collections.Specialized.INotifyCollectionChanged nodesCollection)
            {
                nodesCollection.CollectionChanged += (sender, e) =>
                {
                    // Direct array modification detected
                    if (!_graphRefreshPending)
                    {
                        _graphRefreshPending = true;
                        _graphRefreshTimer.Stop();
                        _graphRefreshTimer.Start();
                    }
                };
            }
        }
    }

    private void OnSaveCompleted(object sender, EventArgs e)
    {
        // A save happened, so the underlying data model is now up-to-date.
        // We need to refresh our graph with the new data.
        if (_fullScreenGraphViewModel != null && sender is WolvenKit.App.ViewModels.Documents.RedDocumentViewModel docVm)
        {
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                _fullScreenGraphViewModel.UpdateDataAndReload(docVm.Cr2wFile.RootChunk);
                // After the save and refresh, bring focus back to the fullscreen window.
                _fullScreenWindow?.Activate();
            }), System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    private void UpdateFullScreenWindowTitle(WolvenKit.App.ViewModels.Documents.RedDocumentViewModel docViewModel)
    {
        if (_fullScreenWindow != null)
        {
            var filename = System.IO.Path.GetFileName(docViewModel.FilePath);
            var dirtyIndicator = docViewModel.IsDirty ? "*" : "";
            _fullScreenWindow.SetCurrentValue(System.Windows.Window.TitleProperty, $"WolvenKit - {filename}{dirtyIndicator} - Full Screen");
        }
    }

    private void OnDocumentPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "IsDirty" && sender is WolvenKit.App.ViewModels.Documents.RedDocumentViewModel docViewModel)
        {
            UpdateFullScreenWindowTitle(docViewModel);
            // Also rebuild breadcrumb to update dirty indicator
            BuildBreadcrumb();
        }
    }

    private void OnFullScreenDocumentPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "IsDirty" && sender is WolvenKit.App.ViewModels.Documents.RedDocumentViewModel docViewModel && _fullScreenBreadcrumb != null)
        {
            // Update the filename element in the breadcrumb
            if (_fullScreenBreadcrumb.Children[0] is System.Windows.Controls.TextBlock fileElement)
            {
                var filename = System.IO.Path.GetFileName(docViewModel.FilePath);
                var dirtyIndicator = docViewModel.IsDirty ? "*" : "";
                fileElement.Text = $"{filename}{dirtyIndicator}";
            }
        }
    }
}
