using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDistributedMemoryCache(); // Enables in-memory caching for sessions
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30); 
    options.Cookie.HttpOnly = true;                 
    options.Cookie.IsEssential = true;              // Ensure cookie is essential for session
});

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
});

var app = builder.Build();

// Enable session middleware
app.UseSession();

app.MapGet("/users", async (ApplicationDbContext db) => {
    var users = await db.EmployeeDetails.ToListAsync();
    return users.Any() ? Results.Ok(users) : Results.NoContent();
});

app.MapGet("/users/{id}", async (int id, ApplicationDbContext db) => {
    var user = await db.EmployeeDetails.FindAsync(id);
    return user is not null ? Results.Ok(user) : Results.NotFound();
});

app.MapPost("/registration", async (HttpContext context,EmployeeDetails employeeDetails, ApplicationDbContext db) => {
    if (employeeDetails == null) return Results.BadRequest("User is null");
    var userIdString = context.Session.GetString("UserId");
    var role = context.Session.GetString("role");
    if (userIdString == null || role == null || role != "admin") {
        var errorMessage = new {
            message = "you are not allowed to add the details",
            error = "per"
        };
        return Results.Json(errorMessage, statusCode: 404);
    }
    
    employeeDetails.Id = 0;
    db.EmployeeDetails.Add(employeeDetails);
    await db.SaveChangesAsync();
    return Results.Created($"/users/{employeeDetails.Id}", employeeDetails);
        
});

app.MapPost("/login", async (HttpContext context, LoginDetails loginDetails, ApplicationDbContext db) => {
    var user = await db.EmployeeDetails.FirstOrDefaultAsync(u => u.Email == loginDetails.Email);

    if (user == null) {
        var message = new
        {
            message = "Login Failed"
        };
        return Results.Json(message, statusCode: 404); }

    if (user.Password == loginDetails.Password) {
        context.Session.SetString("UserId", user.Id.ToString());
        context.Session.SetString("role", user.Role);

        var successMessage = new
        {
            message = "Login successful",
            UserId = user.Id
        };
        return Results.Json(successMessage, statusCode: 200);
    }
    else {
        var errorMessage = new
        {
            message = "Invalid password"
        };
        return Results.Json(errorMessage, statusCode: 401);
    }
});

app.MapGet("/dashboard", async (HttpContext context, ApplicationDbContext db) =>
{
    var userIdString = context.Session.GetString("UserId");
    if (string.IsNullOrEmpty(userIdString))
    {
        var errorMessage = new
        {
            message = "User is not logged in"
        };
        return Results.Json(errorMessage, statusCode: 401);
    }

    // Convert userId to an integer
    if (!int.TryParse(userIdString, out int userId))
    {
        var errorMessage = new { message = "Invalid user ID" };
        return Results.Json(errorMessage, statusCode: 400);
    }

    // Find the user by their ID
    var user = await db.EmployeeDetails.FindAsync(userId);

    if (user == null) {
        var errorMessage = new { message = "User not found" };
        return Results.Json(errorMessage, statusCode: 404);
    }

    var message = new
    {
        message = "Welcome to your dashboard",
        UserId = userId,
        Email = user.Email,
        role = user.Role
    };

    return Results.Ok(message);
});


app.MapPut("/users/{id}", async (HttpContext context,int id, EmployeeDetails updatedUser, ApplicationDbContext db) => {
    var userIdString = context.Session.GetString("UserId");
    var role = context.Session.GetString("role");
    if (userIdString == null || role == null || role != "admin") {
        var errorMessage = new {
            message = "you are not allowed to change the details",
            error = "per"
        };
        return Results.Json(errorMessage, statusCode: 404);
    }    
    var user = await db.EmployeeDetails.FindAsync(id);
    if (user == null) { return Results.NotFound(); }

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

    public DbSet<EmployeeDetails> EmployeeDetails { get; set; }
}

public class EmployeeDetails
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public string Password { get; set; }
    public string Role { get; set; }
}

public class LoginDetails
{
    public string Email { get; set; }
    public string Password { get; set; }
}
