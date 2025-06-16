using Microsoft.ML.Data;

namespace Xvirus.Model
{
    public class ModelInput
    {
        [LoadColumn(0)]
        [ColumnName(@"Label")]
        public string Label { get; set; }

        [LoadColumn(1)]
        [ColumnName(@"ImageSource")]
        public byte[] ImageSource { get; set; }

    }
}
