using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

namespace TaskApi
{
    public class RabbitMQService : IRabbitMQService, IAsyncDisposable
    {
        private readonly IConnectionFactory _factory;

        private IConnection _connection;

        private IChannel _channel;

        private readonly SemaphoreSlim _lock = new(1, 1);

        public RabbitMQService(IConnectionFactory factory)
        {
            _factory = factory;
        }

        public async Task PublishAsync<T>(string exchange, string queueName, T message)
        {
            var channel = await GetChannelAsync();

            await channel.ExchangeDeclareAsync(exchange: exchange, type: ExchangeType.Direct, durable: true);
            
            var args = new Dictionary<string, object?> { { "x-queue-type", "quorum" } };
            await channel.QueueDeclareAsync(queue: queueName, durable: true, exclusive: false, autoDelete: false, arguments: args);
            string routingKey = queueName;
            await channel.QueueBindAsync(queue: queueName, exchange: exchange, routingKey: routingKey);
            
            var json = JsonSerializer.Serialize(message);
            var body = Encoding.UTF8.GetBytes(json);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            await channel.BasicPublishAsync(exchange: exchange, routingKey: routingKey, body: body, cancellationToken: cts.Token);
        }

        private async Task<IChannel> GetChannelAsync()
        {
            if (_channel != null) return _channel;

            await _lock.WaitAsync();
            try
            {
                if (_channel == null)
                {
                    _connection = await _factory.CreateConnectionAsync();
                    _channel = await _connection.CreateChannelAsync(new CreateChannelOptions(true, false));
                }
                return _channel;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _lock.WaitAsync();
            try
            {
                if (_channel != null)
                {
                    await _channel.CloseAsync();
                    _channel.Dispose();
                }

                if (_connection != null)
                {
                    await _connection.CloseAsync();
                    _connection.Dispose();
                }
            }
            finally
            {
                _lock.Release();
            }

            _lock.Dispose();
        }
    }

    public interface IRabbitMQService
    {
        Task PublishAsync<T>(string exchange, string queueName, T message);
    }
}
