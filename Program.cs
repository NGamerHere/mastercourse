using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularOrigins", builder =>
    {
        builder.WithOrigins("http://localhost:4200") 
               .AllowAnyMethod()
               .AllowAnyHeader()
               .AllowCredentials();
    });
});




// Add services to the container.
builder.Services.AddDistributedMemoryCache(); 
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30); 
    options.Cookie.HttpOnly = true;                 
    options.Cookie.IsEssential = true;
});


builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
});

var app = builder.Build();

app.UseCors("AllowAngularOrigins");
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
        return Results.Json(new { message = "Login Failed" }, statusCode: 404);
    }

    if (user.Password == loginDetails.Password) {
        context.Session.SetString("UserId", user.Id.ToString());
        context.Session.SetString("role", user.Role);
        Console.WriteLine(user.Id.ToString());
        Console.WriteLine(user.Role);   
        return Results.Json(new { message = "Login successful", UserId = user.Id }, statusCode: 200);
    }

    return Results.Json(new { message = "Invalid password" }, statusCode: 401);
});

app.MapGet("/dashboard", async (HttpContext context, ApplicationDbContext db) => {
    var userIdString = context.Session.GetString("UserId");
    
    if (string.IsNullOrEmpty(userIdString)) {
        Console.WriteLine(userIdString);
        Console.WriteLine("this is called");
        return Results.Json(new { message = "User is not logged in", error = "notLogged" }, statusCode: 401);
    }

    if (!int.TryParse(userIdString, out int userId)) {
        return Results.Json(new { message = "Invalid user ID" }, statusCode: 400);
    }

    var user = await db.EmployeeDetails.FindAsync(userId);
    
    if (user == null) {
        return Results.Json(new { message = "User not found" }, statusCode: 404);
    }

    return Results.Ok(new { message = "Welcome to your dashboard", UserId = userId, Email = user.Email, role = user.Role });
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

app.MapPost("/logout", (HttpContext context) => {
    context.Session.Clear(); // Clear the session
    return Results.Ok(new { message = "Logout successful" });
});

    app.MapGet("/courses", async (ApplicationDbContext db) =>
    await db.Courses.ToListAsync());


app.MapGet("/employee-progress", async (ApplicationDbContext db) =>
    await db.EmployeeProgress.ToListAsync());


app.MapPost("/employee-progress", async (HttpContext context, EmployeeProgress progress, ApplicationDbContext db) =>
{
    var employeeIdString = context.Session.GetString("UserId");

    if (string.IsNullOrEmpty(employeeIdString) || !int.TryParse(employeeIdString, out int employeeId))
    {
        var message = new { message = "User is not logged in" };
        return Results.Json(message,statusCode:401);
    }

    progress.EmployeeID = employeeId; 

    db.EmployeeProgress.Add(progress);
    await db.SaveChangesAsync();
    return Results.Created($"/employee-progress/{progress.Id}", progress);
});

app.MapPut("/employee-progress/{id}", async (HttpContext context, int id, EmployeeProgress updatedProgress, ApplicationDbContext db) => {
    var employeeIdString = context.Session.GetString("UserId");

    if (string.IsNullOrEmpty(employeeIdString) || !int.TryParse(employeeIdString, out int employeeId))
    {
        var message = new { message = "User is not logged in" };
        return Results.Json(message, statusCode: 401);
    }

    var progress = await db.EmployeeProgress.FindAsync(id);
    if (progress == null) {
        return Results.NotFound();
    }

    
    if (progress.EmployeeID != employeeId) {
        return Results.Json(new { message = "You are not allowed to update this progress entry" }, statusCode: 403);
    }

    
    if (updatedProgress.ModulesCompleted > 0) { progress.ModulesCompleted = updatedProgress.ModulesCompleted; }

    if (updatedProgress.TotalModule > 0) { progress.TotalModule = updatedProgress.TotalModule; }

    if (progress.TotalModule > 0)
    {
        progress.Progress = (float)progress.ModulesCompleted / progress.TotalModule * 100;
    }
    else
    {
        progress.Progress = 0;
    }

    await db.SaveChangesAsync();
    return Results.Ok(progress);
});


app.MapGet("/completed-courses", async (HttpContext context, ApplicationDbContext db) => {
    var employeeIdString = context.Session.GetString("UserId");

    if (string.IsNullOrEmpty(employeeIdString) || !int.TryParse(employeeIdString, out int employeeId))
    {
        return Results.Json(new { message = "User is not logged in" }, statusCode: 401);
    }

    var completedCourses = await db.EmployeeProgress
        .Where(ep => ep.EmployeeID == employeeId && ep.Progress == 100.0)
        .Join(db.Courses,
            ep => ep.CourseID,
            course => course.Id,
            (ep, course) => new 
            {
                CourseId = course.Id,
                CourseName = course.CourseName,
                Details = course.Details,
                ModulesCompleted = ep.ModulesCompleted,
                TotalModules = ep.TotalModule,
                Progress = ep.Progress
            })
        .ToListAsync();

    return completedCourses.Any() ? Results.Ok(completedCourses) : Results.NoContent();
});






app.Run();

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<EmployeeDetails> EmployeeDetails { get; set; }
    public DbSet<Course> Courses { get; set; }
    public DbSet<EmployeeProgress> EmployeeProgress { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EmployeeProgress>()
            .HasOne<EmployeeDetails>()
            .WithMany()
            .HasForeignKey(ep => ep.EmployeeID);

        modelBuilder.Entity<EmployeeProgress>()
            .HasOne<Course>()
            .WithMany()
            .HasForeignKey(ep => ep.CourseID);
    }
}


    
public class EmployeeDetails
{
    public int Id { get; set; }
    public string  Name { get; set; }
    public string Email { get; set; }
    public string Password { get; set; }
    public string Role { get; set; }
}

public class LoginDetails
{
    public string Email { get; set; }
    public string Password { get; set; }
}

public class Course
{
    public int Id { get; set; }
    public string PlayListID { get; set; }
    public string CourseName { get; set; }
    public string Details { get; set; }
}

public class EmployeeProgress
{
    public int Id { get; set; }
    public int EmployeeID { get; set; }
    public int CourseID { get; set; }
    public float Progress { get; set; }
    public int ModulesCompleted { get; set; }
    public int TotalModule { get; set; }
}

