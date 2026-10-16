using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;

namespace TaskApi
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            var connectionString = builder.Configuration.GetConnectionString("PostgresConnection");

            builder.Services.AddDbContext<TaskDbContext>(opt => opt.UseNpgsql(connectionString));

            builder.Services.AddSingleton<IConnectionFactory>(sp => new ConnectionFactory
            {
                HostName = builder.Configuration["RabbitMQ:Host"],
                UserName = builder.Configuration["RabbitMQ:User"],
                Password = builder.Configuration["RabbitMQ:Password"]
            });

            builder.Services.AddSingleton<IRabbitMQService, RabbitMQService>();

            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();


            var app = builder.Build();


            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<TaskDbContext>();

                db.Database.Migrate();
            }


            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();

                app.MapSwaggerUI();
            }


            app.MapEndPoints();

            app.Run();
        }
    }
}
