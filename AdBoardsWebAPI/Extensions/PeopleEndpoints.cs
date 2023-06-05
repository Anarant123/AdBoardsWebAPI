using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AdBoardsWebAPI.Auth;
using AdBoardsWebAPI.Contracts.Requests.Models;
using AdBoardsWebAPI.Data;
using AdBoardsWebAPI.Data.Models;
using AdBoardsWebAPI.DomainTypes.Enums;
using AdBoardsWebAPI.Options;
using MailKit.Net.Smtp;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MimeKit;
using MimeKit.Text;

namespace AdBoardsWebAPI.Extensions;

public static class PeopleEndpoints
{
    public static WebApplication MapPeopleEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("People");

        group.MapGet("GetPeople", async (AdBoardsContext context) =>
        {
            var people = await context.People.ToListAsync();
            return people.Count == 0 ? Results.NotFound() : Results.Ok(people);
        }).RequireAuthorization(Policies.Admin);

        group.MapGet("GetCountOfClient", async (AdBoardsContext context) =>
        {
            var count = await context.People.CountAsync();
            return count == 0 ? Results.NotFound() : Results.Ok(count);
        }).RequireAuthorization(Policies.Admin);

        group.MapPost("Authorization", async (string login, string password, AdBoardsContext context,
            IOptions<JwtOptions> jwtOptions) =>
        {
            var person = await context.People.FirstOrDefaultAsync(x => x.Login == login && x.Password == password);
            if (person is null) return Results.BadRequest();

            var key = Encoding.ASCII.GetBytes(jwtOptions.Value.Key);
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(JwtRegisteredClaimNames.Sub, person.Id.ToString()),
                    new Claim("id", person.Id.ToString()),
                    new Claim("email", person.Email),
                    new Claim("login", person.Login),
                    new Claim("rightId", person.RightId.ToString())
                }, "jwt"),
                Expires = DateTime.UtcNow.AddDays(7),
                Issuer = jwtOptions.Value.Issuer,
                Audience = jwtOptions.Value.Audience,
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key),
                    SecurityAlgorithms.HmacSha512Signature)
            };
            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);
            var stringToken = tokenHandler.WriteToken(token);

            return Results.Ok(new { person, Token = stringToken });
        }).AllowAnonymous();

        group.MapPost("Registration", async (RegisterModel model, AdBoardsContext context, FileManager fileManager) =>
        {
            var p = new Person
            {
                Login = model.Login,
                Password = model.Password,
                Name = model.Name,
                City = model.City,
                Birthday = model.Birthday,
                Phone = model.Phone,
                Email = model.Email,
                RightId = RightType.Normal,
                PhotoName = await fileManager.SaveUserPhoto(null)
            };

            context.People.Add(p);

            try
            {
                await context.SaveChangesAsync();
            }
            catch (DbUpdateException e)
            {
                Console.WriteLine(e);
                return Results.Conflict();
            }

            return Results.Ok(p);
        }).AllowAnonymous();

        group.MapPost("RecoveryPassword", async (AdBoardsContext dbContext, IOptions<SmtpOptions> smtpOptions,
            ClaimsPrincipal user) =>
        {
            var id = int.Parse(user.Claims.FirstOrDefault(x => x.Type == "id")!.Value);

            var p = await dbContext.People.FindAsync(id);
            if (p is null) return Results.NotFound();

            var smtp = smtpOptions.Value;

            using var emailMessage = new MimeMessage
            {
                Subject = "Восстановление пароля",
                Body = new TextPart(TextFormat.Html)
                {
                    Text = "Ваш пароль от AdBoards: " + p.Password
                }
            };
            emailMessage.From.Add(new MailboxAddress("Администрация сайта", smtp.Address));
            emailMessage.To.Add(new MailboxAddress("", p.Email));

            using var client = new SmtpClient();

            try
            {
                await client.ConnectAsync(smtp.Host, smtp.Port, false);
                await client.AuthenticateAsync(smtp.Username, smtp.Password);
                await client.SendAsync(emailMessage);
                await client.DisconnectAsync(true);

                return Results.Ok();
            }
            catch
            {
                return Results.BadRequest();
            }
        });

        group.MapPut("Update", async (UpdatePersonModel model, AdBoardsContext context, ClaimsPrincipal user) =>
        {
            var id = int.Parse(user.Claims.FirstOrDefault(x => x.Type == "id")!.Value);

            var person = await context.People.FindAsync(id);
            if (person is null) return Results.NotFound();

            person.Name = model.Name;
            person.City = model.City;

            if (model.Birthday is not null) person.Birthday = model.Birthday.Value;
            if (model.Phone is not null) person.Phone = model.Phone;
            if (model.Email is not null) person.Email = model.Email;

            try
            {
                await context.SaveChangesAsync();
            }
            catch (DbUpdateException e)
            {
                Console.WriteLine(e);
                return Results.Conflict();
            }

            return Results.Ok(person);
        });

        group.MapPut("Photo", async (IFormFile? photo, AdBoardsContext context, FileManager fileManager,
            ClaimsPrincipal user) =>
        {
            var id = int.Parse(user.Claims.FirstOrDefault(x => x.Type == "id")!.Value);

            var person = await context.People.FindAsync(id);
            if (person is null) return Results.NotFound();

            person.PhotoName = await fileManager.SaveUserPhoto(photo);

            await context.SaveChangesAsync();

            return Results.Ok();
        });

        group.MapDelete("Delete", async (string login, AdBoardsContext context) =>
        {
            var p = await context.People.FirstOrDefaultAsync(x => x.Login == login);
            if (p is null) return Results.NotFound();

            context.People.Remove(p);
            await context.SaveChangesAsync();

            return Results.Ok();
        }).RequireAuthorization(Policies.Admin);

        return app;
    }
}