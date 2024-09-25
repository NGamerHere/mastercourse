using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
});

var app = builder.Build();

// Configure the HTTP request pipeline.
app.MapGet("/", () => "Hello World!");



app.MapGet("/users", async (ApplicationDbContext db) =>{
    var users = await db.EmployeeDetails.ToListAsync();
    return users.Any() ? Results.Ok(users) : Results.NoContent();
});

app.MapGet("/users/{id}", async (int id, ApplicationDbContext db) =>
{
    var user = await db.EmployeeDetails.FindAsync(id);
    return user is not null ? Results.Ok(user) : Results.NotFound();
});

app.MapPost("/add",async (EmployeeDetails employeeDetails, ApplicationDbContext db) =>
{
    if (employeeDetails == null)
    {
        return Results.BadRequest("User is null");
    }
    employeeDetails.Id = 0;
    db.EmployeeDetails.Add(employeeDetails);
    await db.SaveChangesAsync();
    return Results.Created($"/users/{employeeDetails.Id}", employeeDetails);
});

app.MapPut("/users/{id}", async (int id, EmployeeDetails updatedUser, ApplicationDbContext db) =>
{
    var user = await db.EmployeeDetails.FindAsync(id);
    if (user == null)
    {
        return Results.NotFound();
    }

    user.Name = updatedUser.Name ?? user.Name;
    user.Email = updatedUser.Email ?? user.Email;
    user.Password = updatedUser.Password ?? user.Password;
    user.Role = updatedUser.Role ?? user.Role;

    await db.SaveChangesAsync();
    return Results.Ok(user);
});

app.Run();

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    // Define your DbSet properties here
    // public DbSet<Employee> Employees { get; set; }

    public DbSet<EmployeeDetails> EmployeeDetails{get; set;}

    
    
}

public class EmployeeDetails{
   public int Id {get ; set ;}
   public string Name { get ; set ; }

   public string Email { get; set; }
   public string Password {get ; set;}
   public string Role { get ; set ; }
}