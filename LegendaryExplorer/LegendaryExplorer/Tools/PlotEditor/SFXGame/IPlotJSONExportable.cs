using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace LegendaryExplorer.Tools.PlotEditor.SFXGame
{
    internal interface IPlotJSONExportable
    {
        public JsonObject ToJsonObject();
    }
}
