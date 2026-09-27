using System.Threading.Channels;

namespace Portion.Server.Infrastructure
{
    public record IngestionTask(Guid ResumeId, string FilePath);

    public class IngestionChannel
    {
        private readonly Channel<IngestionTask> _channel;

        public IngestionChannel()
        {
            var options = new BoundedChannelOptions(200)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false
            };
            _channel = Channel.CreateBounded<IngestionTask>(options);
        }

        public ChannelWriter<IngestionTask> Writer => _channel.Writer;
        public ChannelReader<IngestionTask> Reader => _channel.Reader;
    }
}
