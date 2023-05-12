using Autofac;
using Autofac.Extensions.DependencyInjection;
using LayuiAdminNetCore.Appsettings;
using LayuiAdminNetCore.AuthorizationModels;
using LayuiAdminNetPro.Utilities.Autofac;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.WebEncoders;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

builder.Services.AddControllers().AddNewtonsoftJson(options =>
{
    //����ѭ������
    options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
    //С�շ�
    options.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
});

#region MemoryCache

builder.Services.AddMemoryCache();

#endregion MemoryCache

#region Cors

builder.Services.AddCors(policy =>
{
    policy.AddPolicy("CorsPolicy", opt =>
     //opt.WithOrigins(appSettings.CrosAddress)
     opt.AllowAnyOrigin()
     .AllowAnyHeader()
     .AllowAnyMethod()
     .WithExposedHeaders("X-Pagination")
    );
});

#endregion Cors

#region ���MVC��ͼ�е����ı�html���������

builder.Services.Configure<WebEncoderOptions>(options =>
{
    options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All);
});

#endregion ���MVC��ͼ�е����ı�html���������

#region Autofac IOC ����

builder.Host.UseServiceProviderFactory(new AutofacServiceProviderFactory()); //ͨ�������滻����Autofac���Ͻ���

//https://blog.csdn.net/xiaxiaoying2012/article/details/84617677
//http://niuyi.github.io/blog/2012/04/06/autofac-by-unit-test/
//https://www.cnblogs.com/gygtech/p/14491364.html

builder.Host.ConfigureContainer<ContainerBuilder>(containerBuilder =>
{
    /* ͨ���̳� IDenpendency ����������ע�� */
    //Type basetype = typeof(IDenpendency);
    //containerBuilder.RegisterAssemblyTypes(Assembly.GetExecutingAssembly())
    //.Where(t => basetype.IsAssignableFrom(t) && t.IsClass)
    //.AsImplementedInterfaces().InstancePerLifetimeScope();

    //containerBuilder.RegisterGeneric(typeof(IList<>)).As(typeof(IList<>));

    /*  Middleware */
    var adminAssembly = Assembly.Load("LayuiAdminNetPro");
    containerBuilder.RegisterAssemblyTypes(adminAssembly).Where(t => t.Name.EndsWith("Handler")).AsImplementedInterfaces().InstancePerLifetimeScope();

    /* Ȩ������ע�� */
    var authorizeAssembly = Assembly.Load("LayuiAdminNetGate");
    containerBuilder.RegisterAssemblyTypes(authorizeAssembly).Where(t => t.IsClass && (t.Name.EndsWith("Service") || t.Name.EndsWith("Handler"))).AsImplementedInterfaces().InstancePerLifetimeScope();

    /* ҵ���߼�ע�� */
    var serverAssembly = Assembly.Load("LayuiAdminNetServer");
    containerBuilder.RegisterAssemblyTypes(serverAssembly).Where(t => t.IsClass && t.Name.EndsWith("Service")).AsImplementedInterfaces().InstancePerLifetimeScope(); //InstancePerLifetimeScope ��֤�����������ڻ�������

    /* �ִ�����ע�� */
    var repositoryAssembly = Assembly.Load("LayuiAdminNetInfrastructure");
    containerBuilder.RegisterAssemblyTypes(repositoryAssembly).Where(t => t.IsClass && t.Name.EndsWith("Repository")).AsImplementedInterfaces().InstancePerLifetimeScope();

    /* ע��ÿ���������ͳ���֮��Ĺ�ϵ */
    var controllerBaseType = typeof(ControllerBase);
    containerBuilder.RegisterAssemblyTypes(typeof(Program).Assembly)
    .Where(t => controllerBaseType.IsAssignableFrom(t) && t != controllerBaseType)
    .PropertiesAutowired(new CustomPropertySelector()); //ע������ע��
});

//֧�ֿ�������ʵ����IOC���������� -- autofac������
builder.Services.Replace(ServiceDescriptor.Transient<IControllerActivator, ServiceBasedControllerActivator>());

#endregion Autofac IOC ����

//��ȡ appsetings �е�������Ϣ
var appSection = builder.Configuration.GetSection("AppSettings");
builder.Services.Configure<AppSettings>(option => appSection.Bind(option));
var appSettings = appSection.Get<AppSettings>();

/* JWT[Json web token] */
builder.Services
    // ������Ȩ����Ҳ���Ǿ���Ĺ����Ѿ���Ӧ��Ȩ�޲��ԣ����繫˾��ͬȨ�޵��Ž���
    .AddAuthorization(options =>
    {
        // �Զ�����ڲ��Ե���ȨȨ��   [Authorize(Policy= Policys.Permission)]
        options.AddPolicy(Policys.Admin, policy => policy.Requirements.Add(new PermissionRequirement()));
    })
    // ������֤����
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    // ����JWT
    .AddJwtBearer(options =>
    {
        options.Events = new JwtBearerEvents()
        {
            OnMessageReceived = context =>
            {
                context.Token = context.Request.Cookies[".AspNetCore.Token"];
                return Task.CompletedTask;
            }
        };
        options.RequireHttpsMetadata = false;
        options.SaveToken = true;
        //Token Validation Parameters��
        options.TokenValidationParameters = new TokenValidationParameters
        {
            //�Ƿ���֤������
            ValidateIssuer = true,
            ValidIssuer = appSettings!.JwtBearer!.Issuer,//������
                                                         //�Ƿ���֤������
            ValidateAudience = true,
            ValidAudience = appSettings!.JwtBearer!.Audience,//������
                                                             //�Ƿ���֤��Կ
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(s: appSettings!.JwtBearer!.SecurityKey)),
            ValidateLifetime = true, //��֤��������
            RequireExpirationTime = true, //����ʱ��
            ClockSkew = TimeSpan.Zero  //����ʱ��ƫбΪ0
        };
    });

var app = builder.Build();

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();  //JWT��֤

app.UseAuthorization();   //JWT��Ȩ

app.UseCors("CorsPolicy");

//app.MapControllerRoute(
//    name: "default",
//    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapControllers();

app.Run();