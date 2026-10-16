using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using TaskApi;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace Tests
{
    public class IntegrationTests : IAsyncLifetime
    {
        private IConnection? _testConnection;

        private IChannel? _testChannel;

        private readonly PostgreSqlContainer _postgresContainer = new PostgreSqlBuilder("postgres:15-alpine")
            .Build();
                
        private readonly RabbitMqContainer _rabbitMqContainer = new RabbitMqBuilder("rabbitmq:3-management")
            .Build();

        private WebApplicationFactory<Program> _factory = null!;

        private HttpClient _client = null!;

        private readonly ConcurrentBag<ComplitedTask> _consumedMessages = new();

        public async Task InitializeAsync()
        {
            await Task.WhenAll(_postgresContainer.StartAsync(), _rabbitMqContainer.StartAsync());

            
            _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    
                    var dbDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<TaskDbContext>));
                    
                    if (dbDescriptor != null) 
                        services.Remove(dbDescriptor);

                    services.AddDbContext<TaskDbContext>(options =>
                        options.UseNpgsql(_postgresContainer.GetConnectionString()));


                    var rabbitFactoryDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IConnectionFactory));

                    if (rabbitFactoryDescriptor != null)
                        services.Remove(rabbitFactoryDescriptor);
                    
                    services.AddSingleton<IConnectionFactory>(sp => new ConnectionFactory
                    {                       
                        Uri = new Uri(_rabbitMqContainer.GetConnectionString())
                    });
                });
            });

            _client = _factory.CreateClient();

            await StartTestConsumerAsync();
        }

        [Fact]
        public async Task CreateAndCompleteTask()
        {            
            var createResponse = await _client.PostAsJsonAsync("/tasks", new CreatedTaskDTO("Задача 1"));

            Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);            
            
            
            var getResponse = await _client.GetAsync("/tasks");

            var tasks = await getResponse.Content.ReadFromJsonAsync<List<TaskDTO>>();            

            Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);            

            Assert.True(tasks is not null && tasks.Any());

            var id = tasks.FirstOrDefault().id;

            var completeResponse = await _client.PutAsync($"/tasks/{id}/complete", null);


            ComplitedTask taskMess = null;
            
            for (int i = 0; i < 42; i++)
            {
                taskMess = _consumedMessages.FirstOrDefault();
                
                if (taskMess != null) 
                    break;
                
                await Task.Delay(100);
            }

            Assert.NotNull(taskMess);

            Assert.Equal(id, taskMess.taskId);
        }

        private async Task StartTestConsumerAsync()
        {
            var factory = new ConnectionFactory { Uri = new Uri(_rabbitMqContainer.GetConnectionString()) };

            _testConnection = await factory.CreateConnectionAsync();
            _testChannel = await _testConnection.CreateChannelAsync();

            string exchangeName = "task.events";
            string queueName = "task.completed";

            await _testChannel.ExchangeDeclareAsync(exchange: exchangeName, type: ExchangeType.Direct, durable: true);

            var args = new Dictionary<string, object?> { { "x-queue-type", "quorum" } };
            await _testChannel.QueueDeclareAsync(queue: queueName, durable: true, exclusive: false, autoDelete: false, arguments: args);

            await _testChannel.QueueBindAsync(queue: queueName, exchange: exchangeName, routingKey: queueName);

            var consumer = new AsyncEventingBasicConsumer(_testChannel);

            consumer.ReceivedAsync += async (m, ea) =>
            {
                try
                {
                    var json = Encoding.UTF8.GetString(ea.Body.ToArray());

                    var msg = JsonSerializer.Deserialize<ComplitedTask>(json);

                    if (msg != null)
                    {
                        _consumedMessages.Add(msg);
                    }
                }
                catch
                {
                    //
                }

                await Task.CompletedTask;
            };

            await _testChannel.BasicConsumeAsync(queue: queueName, autoAck: true, consumer: consumer);
        }

        public async Task DisposeAsync()
        {
            if (_factory != null)
                await _factory.DisposeAsync();
                        
            if (_postgresContainer != null)
                await _postgresContainer.DisposeAsync();

            if (_rabbitMqContainer != null)
                await _rabbitMqContainer.DisposeAsync();
        }
    }
}
