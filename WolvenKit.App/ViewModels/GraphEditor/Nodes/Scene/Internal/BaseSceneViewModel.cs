using System.Windows.Media;
using WolvenKit.RED4.Types;

namespace WolvenKit.App.ViewModels.GraphEditor.Nodes.Scene.Internal;

public abstract class BaseSceneViewModel : NodeViewModel
{
    public override uint UniqueId => ((scnSceneGraphNode)Data).NodeId.Id;

    public Brush Background { get; protected set; }

    protected BaseSceneViewModel(scnSceneGraphNode scnSceneGraphNode) : base(scnSceneGraphNode)
    {
        Title = $"[{UniqueId}] {Data.GetType().Name[3..^4]}";
        Background = GetBackgroundForNodeType(scnSceneGraphNode);
    }

    private static Brush GetBackgroundForNodeType(scnSceneGraphNode node)
    {
        // Create a slight dark tint background based on node type
        return node switch
        {
            scnStartNode => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#33228B22")), // Dark green tint
            scnEndNode => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#33B22222")), // Dark red tint
            scnChoiceNode => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#332288B2")), // Dark blue tint
            scnQuestNode => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#33B2B222")), // Dark yellow tint
            scnSectionNode => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#33B222B2")), // Dark purple tint
            scnRandomizerNode => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3322B2B2")), // Dark cyan tint
            scnHubNode => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#33B2AA22")), // Dark orange tint
            scnAndNode => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3355AA22")), // Dark lime tint
            scnXorNode => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#33AA2255")), // Dark pink tint
            _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#33444444")) // Default dark gray tint
        };
    }
}

public abstract class BaseSceneViewModel<T> : BaseSceneViewModel where T : scnSceneGraphNode
{
    protected T _castedData => (T)Data;

    public BaseSceneViewModel(scnSceneGraphNode scnSceneGraphNode) : base(scnSceneGraphNode)
    {
    }

    internal override void GenerateSockets()
    {
        Input.Add(new SceneInputConnectorViewModel("In", "In", UniqueId, 0));

        for (var i = 0; i < _castedData.OutputSockets.Count; i++)
        {
            Output.Add(new SceneOutputConnectorViewModel($"Out{i}", $"Out{i}", UniqueId, _castedData.OutputSockets[i]));
        }
    }
}