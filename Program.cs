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


app.MapGet("/users", async (ApplicationDbContext db,HttpContext context) => {
    var role = context.Session.GetString("role");

    if (role == null && role == "admin")
    {
        return Results.Json(new {
           message="you are not authorized to access this page",
        }, statusCode:401 );
    }
    
    var users = await db.EmployeeDetails.ToListAsync();
    return users.Any() ? Results.Ok(users) : Results.NoContent();
});

app.MapGet("/users/{id}", async (int id,HttpContext context, ApplicationDbContext db) => {
    var role = context.Session.GetString("role");

    if (role == null && role == "admin")
    {
        return Results.Json(new {
            message="you are not authorized to access this page",
        }, statusCode:401 );
    }
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

    if (user == null) { return Results.Json(new { message = "Login Failed" }, statusCode: 404); }

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
    context.Session.Clear(); 
    return Results.Ok(new { message = "Logout successful" });
});

app.MapGet("/courses", async (ApplicationDbContext db) => await db.Courses.ToListAsync());


app.MapGet("/employee-progress", async (ApplicationDbContext db) =>
    await db.EmployeeProgress.ToListAsync());

app.MapGet("/get_basic_details", async (HttpContext context) => {
    var userIdString = context.Session.GetString("UserId");
    var role = context.Session.GetString("role");
    if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out int userId)) {
        return Results.Json(new { message = "You have logged off", error = "loggedOFF" }, statusCode: 401);
    }

    return Results.Json(new {
        userId = userId,
        role = role
    }, statusCode: 200 );
});

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

app.MapPost("/enroll", async (HttpContext context, EmployeeProgress progress, ApplicationDbContext db) => {
    var employeeIdString = context.Session.GetString("UserId");

    if (string.IsNullOrEmpty(employeeIdString) || !int.TryParse(employeeIdString, out int employeeId)) {
        var message = new { message = "User is not logged in" };
        return Results.Json(message, statusCode: 401);
    }
    
    var course = await db.Courses.FindAsync(progress.CourseID);
    if (course == null) {
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
    var progress = await db.EmployeeProgress
        .FirstOrDefaultAsync(ep => ep.CourseID == courseId && ep.EmployeeID == userId);

    if (progress == null) 
    {
        progress = new EmployeeProgress
        {
            CourseID = courseId,
            EmployeeID = userId,
            Progress = 0, 
            ModulesCompleted = 0, 
            TotalModule = course.totalModule 
        };

        db.EmployeeProgress.Add(progress);
        await db.SaveChangesAsync();
    }

    
    var completedModules = await db.ModuleStatus
        .Where(ms => ms.CourseID == courseId && ms.EmployeeID == userId)
        .Select(ms => new 
        {
            ms.ModuleNo, 
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
        completedModules = completedModules 
    });
});

app.MapGet("/pending-progress", async (HttpContext context, ApplicationDbContext db) => {
    var userIdString = context.Session.GetString("UserId");
    if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out int userId))
    {
        return Results.Json(new { message = "You are logged off", error = "loggedOFF" }, statusCode: 401);
    }
    
    var pendingProgress = await db.EmployeeProgress
        .Where(ep => ep.EmployeeID == userId && ep.Progress > 0 && ep.Progress < 100) 
        .Join(db.Courses,
            ep => ep.CourseID,
            course => course.Id,
            (ep, course) => new
            {
                EmployeeId = ep.EmployeeID,
                CourseId = course.Id,
                CourseName = course.CourseName,
                Progress = ep.Progress,
                ModulesCompleted = ep.ModulesCompleted,
                playlistID=course.PlayListID,
                TotalModules = ep.TotalModule   
            })
        .ToListAsync();

    return pendingProgress.Any() ? Results.Ok(pendingProgress) : Results.NoContent();
});

app.MapGet("/progress-details", async (HttpContext context, ApplicationDbContext db) =>
{
    var userIdString = context.Session.GetString("UserId");
    if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out int userId)) {
        return Results.Json(new { message = "You are logged off", error = "loggedOFF" }, statusCode: 401);
    }

    var progressDetails = await db.EmployeeProgress
        .Where(ep => ep.EmployeeID == userId) 
        .Join(db.Courses,
            ep => ep.CourseID,
            course => course.Id,
            (ep, course) => new
            {
                EmployeeId = ep.EmployeeID,
                CourseId = course.Id,
                CourseName = course.CourseName,
                Progress = ep.Progress,
                ModulesCompleted = ep.ModulesCompleted,
                playlistID=course.PlayListID,
                TotalModules = ep.TotalModule,
            })
        .ToListAsync();

    
    return progressDetails.Any() ? Results.Ok(progressDetails) : Results.NoContent();
});

app.MapGet("/watch-later", async (HttpContext context, ApplicationDbContext db) =>
{
    var userIdString = context.Session.GetString("UserId");
    if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out int userId))
    {
        return Results.Json(new { message = "You are logged off", error = "loggedOFF" }, statusCode: 401);
    }
    
    var watchLaterCourses = await db.EmployeeProgress
        .Where(ep => ep.EmployeeID == userId && ep.Progress == 0) 
        .Join(db.Courses,
            ep => ep.CourseID,
            course => course.Id,
            (ep, course) => new
            {
                EmployeeId = ep.EmployeeID,
                CourseId = course.Id,
                playlistID = course.PlayListID,
                CourseName = course.CourseName,
                Progress = ep.Progress,
                ModulesCompleted = ep.ModulesCompleted,
                TotalModules = course.totalModule,
                Status = "added-to-watch-later"
            })
        .ToListAsync();

    
    return watchLaterCourses.Any() ? Results.Ok(watchLaterCourses) : Results.NoContent();
});

app.MapGet("/course-progress-chart", async (HttpContext context, ApplicationDbContext db) => {
    var userIdString = context.Session.GetString("UserId");

    if (string.IsNullOrEmpty(userIdString) || !int.TryParse(userIdString, out int userId))
    {
        return Results.Json(new { message = "You are logged off", error = "loggedOFF" }, statusCode: 401);
    }
    
    var chartData = await db.EmployeeProgress
        .Where(ep => ep.EmployeeID == userId) // Filter for the logged-in employee
        .Join(db.Courses,
            ep => ep.CourseID,
            course => course.Id,
            (ep, course) => new
            {
                CourseName = course.CourseName,
                Progress = ep.Progress
            })
        .GroupBy(x => x.CourseName)
        .Select(g => new
        {
            CourseName = g.Key,
            AverageProgress = g.Average(x => x.Progress) 
        })
        .ToListAsync();

    
    var chartResponse = new
    {
        labels = chartData.Select(cd => cd.CourseName).ToList(), 
        datasets = new[]
        {
            new
            {
                label = "Course Progress",
                data = chartData.Select(cd => cd.AverageProgress).ToList(), // Progress for y-axis
                backgroundColor = "rgba(75, 192, 192, 0.2)",
                borderColor = "rgba(75, 192, 192, 1)",
                borderWidth = 1
            }
        }
    };

    return Results.Ok(chartResponse);
});

app.MapPost("/admin/add-course", async (HttpContext context, ApplicationDbContext db, Course newCourse) =>
{
    var userRoleString = context.Session.GetString("role");
    if (string.IsNullOrEmpty(userRoleString) || userRoleString == "Admin"){
        return Results.Json(new { message = "You are not authorized to add courses", error = "unauthorized" }, statusCode: 403);
    }
    
    db.Courses.Add(newCourse);
    await db.SaveChangesAsync();

    return Results.Created($"/courses/{newCourse.Id}", newCourse);
});

app.MapGet("/course-enrollment", async (HttpContext context, ApplicationDbContext db) => 
{
    var userIdString = context.Session.GetString("UserId");
    var role = context.Session.GetString("role");
    
    if (string.IsNullOrEmpty(userIdString) || role != "admin") 
    {
        return Results.Json(new { message = "You are not authorized to view this data." }, statusCode: 403);
    }

    var courseEnrollmentData = await db.EmployeeProgress
        .GroupBy(ep => ep.CourseID)
        .Select(group => new {
            CourseName = db.Courses.FirstOrDefault(c => c.Id == group.Key).CourseName,
            TotalStudents = group.Count()
        })
        .ToListAsync();

    return courseEnrollmentData.Any() ? Results.Ok(courseEnrollmentData) : Results.NoContent();
});

app.MapGet("/all-courses", async (HttpContext context, ApplicationDbContext dbContext) =>
{
    var employeeIdString = context.Session.GetString("UserId");
    
    if (string.IsNullOrEmpty(employeeIdString)) {
        return Results.Json(new { message = "You have been logged off" }, statusCode: 401);
    }
    
    if (!int.TryParse(employeeIdString, out var employeeId)) {
        return Results.Json(new { message = "Invalid employee ID" }, statusCode: 400);
    }
    
    var courses = await dbContext.Courses.ToListAsync();
     
    var courseDetails = courses.Select(course =>
    {
        var progress = dbContext.EmployeeProgress
            .FirstOrDefault(ep => ep.CourseID == course.Id && ep.EmployeeID == employeeId);

        
        bool isEnrolled = progress != null;

        return new
        {
            course.Id,
            course.PlayListID,
            course.CourseName,
            course.Details,
            course.totalModule,
            Progress = progress?.Progress,
            ModulesCompleted = progress?.ModulesCompleted,
            Status = isEnrolled ? "Enrolled" : "Not Enrolled", 
            IsEnrolled = isEnrolled 
        };
    });

    return Results.Ok(courseDetails);
});


app.Run("http://localhost:5126/");

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



