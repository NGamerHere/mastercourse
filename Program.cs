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
        return Results.Json(errorMessage, statusCode: 401);
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



app.MapPost("/add_module", async (ApplicationDbContext db, ModuleStatus modelStatus, HttpContext context) => 
{
    if (modelStatus == null) 
        return Results.BadRequest();

    var userIDString = context.Session.GetString("UserId");
    if (string.IsNullOrEmpty(userIDString) || !int.TryParse(userIDString, out int userId)) 
    {
        return Results.Json(new { message = "You have logged off", error = "loggedOFF" }, statusCode: 401);
    }
     
    modelStatus.EmployeeID = userId;

    if (modelStatus.ModuleNo < 1) {
        return Results.BadRequest();
    }

    // Check if the module for the course and employee is already added
    var existingModuleStatus = await db.ModuleStatus
        .FirstOrDefaultAsync(ms => ms.CourseID == modelStatus.CourseID && ms.EmployeeID == userId && ms.ModuleNo == modelStatus.ModuleNo);

    if (existingModuleStatus != null) {
        // Return a message if the module is already added
        return Results.Json(new { message = "Module already added", statusCode = 409 });
    }

    // Fetch the course to get the total number of modules
    var course = await db.Courses.FindAsync(modelStatus.CourseID);
    if (course == null) {
        return Results.Json(new { message = "Course not found" }, statusCode: 404);
    }

    if (modelStatus.ModuleNo > course.totalModule) {
        return Results.Json(new { message = "invalid module no" }, statusCode: 404);
    }

    // Add the new module to the database
    db.ModuleStatus.Add(modelStatus);
    
    // Find the employee progress entry
    var employeeProgress = await db.EmployeeProgress
        .FirstOrDefaultAsync(ep => ep.CourseID == modelStatus.CourseID && ep.EmployeeID == userId);

    
    if (employeeProgress == null) {
        employeeProgress = new EmployeeProgress
        {
            EmployeeID = userId,
            CourseID = modelStatus.CourseID,
            ModulesCompleted = 1,  
            TotalModule = course.totalModule,  
            Progress = (float)1 / course.totalModule * 100
        };
        db.EmployeeProgress.Add(employeeProgress);
    }
    else
    {
        employeeProgress.ModulesCompleted++;
        employeeProgress.TotalModule = course.totalModule;  
        if (employeeProgress.TotalModule > 0)
        {
            employeeProgress.Progress = (float)employeeProgress.ModulesCompleted / employeeProgress.TotalModule * 100;
        }
    }

    // Save changes to the database
    await db.SaveChangesAsync();

    return Results.Created($"/add_module/{modelStatus.Id}", modelStatus);
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

    //if (updatedProgress.TotalModule > 0) { progress.TotalModule = updatedProgress.TotalModule; }

    if (progress.TotalModule > 0)
    {
        progress.Progress = (float)progress.ModulesCompleted / progress.TotalModule * 100;
    }
    else { progress.Progress = 0; }

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
                playlistID=course.PlayListID,
                Details = course.Details,
                ModulesCompleted = ep.ModulesCompleted,
                TotalModules = ep.TotalModule,
                Progress = ep.Progress
            })
        .ToListAsync();

    return completedCourses.Any() ? Results.Ok(completedCourses) : Results.NoContent();
});

app.MapPost("/employee-progress", async (HttpContext context, EmployeeProgress progress, ApplicationDbContext db) =>
{
    var employeeIdString = context.Session.GetString("UserId");

    if (string.IsNullOrEmpty(employeeIdString) || !int.TryParse(employeeIdString, out int employeeId))
    {
        var message = new { message = "User is not logged in" };
        return Results.Json(message, statusCode: 401);
    }
    
    var course = await db.Courses.FindAsync(progress.CourseID);
    if (course == null)
    {
        var message = new { message = "Course not found" };
        return Results.Json(message, statusCode: 404);
    }

    
    var existingProgress = await db.EmployeeProgress
        .FirstOrDefaultAsync(ep => ep.CourseID == progress.CourseID && ep.EmployeeID == employeeId);

    if (existingProgress != null)
    {
        return Results.Ok(existingProgress);
    }
    progress.EmployeeID = employeeId;
    progress.Progress = 0;
    progress.ModulesCompleted = 0;
    progress.TotalModule = course.totalModule;  

    db.EmployeeProgress.Add(progress);
    await db.SaveChangesAsync();
    
    return Results.Created($"/employee-progress/{progress.Id}", progress);
});

app.MapGet("/course-progress/{courseId}", async (int courseId, HttpContext context, ApplicationDbContext db) =>
{
    // Retrieve the User ID from session
    var userIdString = context.Session.GetString("UserId");
    if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out int userId))
    {
        return Results.Json(new { message = "You are logged off", error = "loggedOFF" }, statusCode: 401);
    }

    // Check if the course exists
    var course = await db.Courses.FindAsync(courseId);
    if (course == null)
    {
        return Results.Json(new { message = "Course not found" }, statusCode: 404);
    }

    // Try to fetch the existing progress
    var progress = await db.EmployeeProgress
        .FirstOrDefaultAsync(ep => ep.CourseID == courseId && ep.EmployeeID == userId);

    if (progress == null) // If no progress exists, create a new entry
    {
        progress = new EmployeeProgress
        {
            CourseID = courseId,
            EmployeeID = userId,
            Progress = 0, // Initial progress
            ModulesCompleted = 0, // Initial modules completed
            TotalModule = course.totalModule // Get total modules from the course
        };

        db.EmployeeProgress.Add(progress);
        await db.SaveChangesAsync();
    }

    // Fetch completed modules for the given course and user
    var completedModules = await db.ModuleStatus
        .Where(ms => ms.CourseID == courseId && ms.EmployeeID == userId)
        .Select(ms => new 
        {
            ms.ModuleNo,  // Include any additional fields if needed
        })
        .ToListAsync();

    return Results.Ok(new 
    { 
        courseId = progress.CourseID,
        courseName = course.CourseName,
        courseDetails = course.Details,
        playlistID = course.PlayListID,
        modulesCompleted = progress.ModulesCompleted,
        totalModule = progress.TotalModule,
        progressPercentage = progress.Progress,
        completedModules = completedModules // Completed module information
    });
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
    public DbSet<ModuleStatus> ModuleStatus { get; set; }


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
        
        modelBuilder.Entity<ModuleStatus>()
            .HasOne(m => m.Course)
            .WithMany()
            .HasForeignKey(m => m.CourseID);

        modelBuilder.Entity<ModuleStatus>()
            .HasOne(m => m.EmployeeDetail)
            .WithMany()
            .HasForeignKey(m => m.EmployeeID);
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
    public int totalModule { get; set; }
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

public class ModuleStatus {
    public int Id { get; set; }
    public int CourseID { get; set; }
    public int EmployeeID { get; set; }
    public int ModuleNo { get; set; }
    public Course? Course { get; set; }  
    public EmployeeDetails? EmployeeDetail { get; set; }  
}



