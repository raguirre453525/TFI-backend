using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Application.Services;
using Dsw2025Tpi.Domain.Entities;
using Dsw2025Tpi.Domain.Interfaces;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Dsw2025Tpi.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthenticateController : ControllerBase
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly JwtTokenService _jwtTokenService;

    public AuthenticateController(UserManager<IdentityUser> userManager, SignInManager<IdentityUser> signInManager, 
        JwtTokenService jwtTokenService, RoleManager<IdentityRole> roleManager)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _jwtTokenService = jwtTokenService;
        _roleManager = roleManager;
    }

    /// <summary>
    /// INICIAR SESION
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginModel request)
    {
        // Buscar por username O email
        var user = await _userManager.FindByNameAsync(request.Username)
                   ?? await _userManager.FindByEmailAsync(request.Username);

        if (user == null)
            return Unauthorized("Usuario o email incorrecto.");

        var result = await _signInManager.CheckPasswordSignInAsync(user, request.Password, false);
        if (!result.Succeeded)
            return Unauthorized("Contraseña incorrecta.");

        var roles = await _userManager.GetRolesAsync(user);
        var role = roles.FirstOrDefault() ?? "USER";

        var token = _jwtTokenService.GenerateToken(user.UserName, role);

        var userInfo = new
        {
            Id = user.Id,
            Username = user.UserName,
            Email = user.Email,
            Role = role
        };

        return Ok(new { token, userInfo });
    }



    /// <summary>
    /// REGISTRARSE
    /// </summary>
    /// <remarks>
    /// La contraseña debe contener minimo: 8 caracteres, un numero, una mayuscula y un simbolo. 
    /// </remarks>
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterModel request)
    {
        // 1. Validar si el usuario ya existe
        var userExists = await _userManager.FindByNameAsync(request.Username);
        if (userExists != null)
            return StatusCode(StatusCodes.Status500InternalServerError, new { Status = "Error", Message = "El usuario ya existe!" });

        // 2. Crear la entidad del usuario
        var user = new IdentityUser
        {
            UserName = request.Username,
            Email = request.Email,
            SecurityStamp = Guid.NewGuid().ToString() // Buena práctica agregarlo
        };

        // 3. Guardarlo en la base de datos
        var result = await _userManager.CreateAsync(user, request.Password);

        if (!result.Succeeded)
            return BadRequest(result.Errors);

        // --- CORRECCIÓN CRÍTICA AQUÍ ---
        // Verificamos si el rol "USER" existe. Si no, lo creamos.
        if (!await _roleManager.RoleExistsAsync("USER"))
        {
            await _roleManager.CreateAsync(new IdentityRole("USER"));
        }

        // Si tienes rol ADMIN, también verifica y crea:
        // if (!await _roleManager.RoleExistsAsync("ADMIN"))
        //     await _roleManager.CreateAsync(new IdentityRole("ADMIN"));

        // Ahora sí, asignamos el rol con seguridad
        await _userManager.AddToRoleAsync(user, "USER");
        // -------------------------------

        return Ok(new { Status = "Success", Message = "Usuario creado correctamente" });
    }

}
