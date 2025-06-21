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

namespace WolvenKit.Views.Documents;
/// <summary>
/// Interaktionslogik für RDTGraphView2.xaml
/// </summary>
public partial class RDTGraphView2
{
    private bool _isFullScreen = false;
    private System.Windows.Window _fullScreenWindow = null;

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
            Title = $"WolvenKit - {ViewModel?.MainGraph?.Title ?? "Graph Editor"} - Full Screen",
            WindowStyle = WindowStyle.None,
            WindowState = WindowState.Maximized,
            AllowsTransparency = false,
            Background = System.Windows.Media.Brushes.Black
        };

        // Create a new GraphEditorView for the full screen window
        var fullScreenEditor = new GraphEditorView
        {
            Source = Editor.Source,
            SelectedNode = Editor.SelectedNode,
            SelectedNodes = Editor.SelectedNodes,
            ViewportLocation = Editor.ViewportLocation
        };
        
        _fullScreenWindow.SetCurrentValue(System.Windows.Window.ContentProperty, fullScreenEditor);
        
        // Add escape key handler
        _fullScreenWindow.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape || e.Key == Key.F11)
            {
                ExitFullScreen();
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

        // Sync any changes back to the original editor
        if (_fullScreenWindow.Content is GraphEditorView fullScreenEditor)
        {
            Editor.ViewportLocation = fullScreenEditor.ViewportLocation;
            Editor.SelectedNode = fullScreenEditor.SelectedNode;
            Editor.SelectedNodes = fullScreenEditor.SelectedNodes;
        }

        // Close and cleanup full screen window
        _fullScreenWindow.Close();
        _fullScreenWindow = null;
        
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
}
