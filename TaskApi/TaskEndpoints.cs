using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace TaskApi
{
    public static class TaskEndpoints
    {
        const string _excange = "task.events";
        const string _queue = "task.completed";

        public static void MapEndPoints(this IEndpointRouteBuilder routeBuilder)
        {
            var group = routeBuilder.MapGroup("/tasks")
                .WithDescription("Задачи")
                .WithSummary("Работа с задачами.");


            group.MapGet("/", GetAllTasks)
                .WithSummary("Все задачи")
                .WithDescription("Получить все задачи.");
            
            group.MapPost("/", CreateTask)
                .WithSummary("Создать задачу")
                .WithDescription("Создать задачу. В теле объект с полем title");

            group.MapPut("/{id:guid}/complete", CompleteTask)
                .WithSummary("Завершить задачу")
                .WithDescription("Отметить задачу как выполненную.");

            group.MapDelete("/{id:guid}", DeleteTask)
                .WithSummary("Удалить задачу")
                .WithDescription("Удалить задачу по Id (UUID)");
        }

        private static async Task<IResult> GetAllTasks([AsParameters] ServicesContext services)
        {
            var tasks = await services.DB.Tasks.Select(t =>
            new TaskDTO(t.Id, t.Title, t.IsCompleted, t.CreatedAt, t.CompletedAt, t.Priority.ToString())).ToListAsync();

            return Results.Ok(tasks);
        }

        private static async Task<IResult> CreateTask([FromBody] CreatedTaskDTO createdTask, [AsParameters] ServicesContext services)
        {
            if (string.IsNullOrWhiteSpace(createdTask.title) || createdTask.title.Length > 200)
                return Results.BadRequest();

            var task = new DbTaskItem()
            {
                Id = Guid.NewGuid(),
                Title = createdTask.title,
                IsCompleted = false,
                CreatedAt = DateTimeOffset.UtcNow,
                Priority = Priority.Medium
            };

            services.DB.Tasks.Add(task);

            try
            {
                await services.DB.SaveChangesAsync();


            }
            catch (Exception e)
            {
                services.Logger.LogWarning(e, "Алярм!!! Ошибка при добавлении задачи в БД!");
                return Results.BadRequest();
            }            

            return Results.Ok();
        }

        private static async Task<IResult> CompleteTask(Guid id, [AsParameters] ServicesContext services)
        {
            var task = await services.DB.Tasks.FindAsync(id);

            if (task == null)
                return Results.NotFound();

            if (task.IsCompleted == true)
                return Results.BadRequest();

            try
            {
                task.IsCompleted = true;
                task.CompletedAt = DateTimeOffset.UtcNow;

                await services.DB.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException) 
            {
                return Results.Conflict();
            }

            var complitedTask = new ComplitedTask(task.Id, task.Title, task.CompletedAt.Value, task.Priority.ToString());

            _ = Task.Run(async () =>
            {
                try
                {
                    await services.MQService.PublishAsync(_excange, _queue, complitedTask);
                }
                catch (Exception e)                
                {
                    services.Logger.LogWarning(e,"Алярм!!! Брокер устал!");
                }
            });


            return Results.Ok();
        }

        private static async Task<IResult> DeleteTask(Guid id, [AsParameters] ServicesContext services)
        {
            try
            {
               var rows = await services.DB.Tasks.Where(t => t.Id == id).ExecuteDeleteAsync();  
                
                if (rows < 1)
                    return Results.NotFound();
            }
            catch (Exception e) 
            {
                services.Logger.LogWarning(e, "Алярм!!! Ошибка при удалении из БД!");
                return Results.BadRequest();
            }

            return Results.Ok();
        }
    }

    public record ServicesContext(HttpContext HttpContext, TaskDbContext DB, IRabbitMQService MQService, ILogger<Program> Logger);

    public record TaskDTO(Guid id, string title, 
        bool isCompleteted, DateTimeOffset createdAt, 
        DateTimeOffset? completedAt, string priority);

    public record CreatedTaskDTO([property: Required(AllowEmptyStrings = false)]  string title);

    public record ComplitedTask(Guid taskId, string title, DateTimeOffset complitedAt, string priority);
}
