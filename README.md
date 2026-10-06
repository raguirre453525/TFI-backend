# TFI Backend — API de comercio electrónico

API académica de Desarrollo de Software para gestionar productos y pedidos,
registrar usuarios y autenticar mediante JWT. Incluye persistencia en SQL Server
y operaciones administrativas autorizadas por rol.

**Repositorios:** [Backend](https://github.com/raguirre453525/TFI-backend) ·
[Frontend React](https://github.com/raguirre453525/TFI-frontend).

## Tecnologías y arquitectura

| Área | Tecnología |
| --- | --- |
| API | C# / .NET 8, ASP.NET Core Web API |
| Autenticación | Identity y JWT Bearer 8.0.17; bibliotecas JWT 7.1.2 |
| Persistencia | SQL Server; EF Core SQL Server 9.0.6, diseño y herramientas 9.0.7 |
| Documentación | Swagger mediante Swashbuckle 6.6.2 |

El flujo principal es **controladores → servicios → repositorios/EF Core → entidades**.

```text
Dsw2025Tpi.sln                 Solución del backend
Dsw2025Tpi.Api/                Controladores, autenticación y configuración
Dsw2025Tpi.Application/        Servicios y modelos de solicitudes/respuestas
Dsw2025Tpi.Data/               Contexto, repositorios y migraciones
Dsw2025Tpi.Domain/             Entidades e interfaces
test/                         Ejemplos manuales y comprobación de arranque
```

## Requisitos

- SDK de .NET 8 y runtime de ASP.NET Core 8. Un SDK más reciente, por sí solo,
  no garantiza que esté instalado el runtime requerido.
- SQL Server Express LocalDB en Windows, o una instancia accesible de SQL Server
  con la conexión configurada externamente.
- `dotnet-ef` 9.0.7 para aplicar las migraciones.
- Certificado HTTPS de desarrollo confiable para utilizar el frontend.

Ejecutar los siguientes comandos en PowerShell **desde la raíz de este repositorio**.

## 1. Configurar el entorno

La conexión predeterminada usa `(localdb)\MSSQLLocalDB`, la base `Dsw2025Db` y
autenticación integrada. `Dsw2025Tpi.Api/appsettings.json` contiene únicamente
valores públicos: no incluye clave JWT ni credenciales administrativas.

| Configuración | Variable de entorno | Uso |
| --- | --- | --- |
| `Jwt:Key` | `Jwt__Key` | Obligatoria; clave aleatoria y exclusiva del entorno, de al menos 32 bytes UTF-8 |
| `Jwt:Issuer`, `Jwt:Audience`, `Jwt:ExpireInMinutes` | `Jwt__Issuer`, `Jwt__Audience`, `Jwt__ExpireInMinutes` | Valores públicos predeterminados en `appsettings.json` |
| `ConnectionStrings:Dsw2025TpiEntities` | `ConnectionStrings__Dsw2025TpiEntities` | Reemplazar la conexión LocalDB cuando corresponda |
| `BootstrapAdmin:Email` | `BootstrapAdmin__Email` | Correo del administrador inicial, opcional |
| `BootstrapAdmin:UserName` | `BootstrapAdmin__UserName` | Nombre de usuario del administrador inicial |
| `BootstrapAdmin:Password` | `BootstrapAdmin__Password` | Contraseña del administrador inicial, nunca versionada |

Para ingresar una clave JWT sin escribir su valor en el comando:

```powershell
$env:Jwt__Key = [System.Net.NetworkCredential]::new('', (Read-Host 'Clave JWT del entorno' -AsSecureString)).Password
```

Esta variable se aplica a la terminal y sus procesos hijos. El proyecto también
admite User Secrets en `Development`, sin modificar el código:

```powershell
dotnet user-secrets set "Jwt:Key" "$env:Jwt__Key" --project .\Dsw2025Tpi.Api\Dsw2025Tpi.Api.csproj
```

User Secrets guarda valores fuera del repositorio, pero no los cifra. En otros
entornos, utilizar variables de entorno o un almacén seguro.

**Administrador inicial:** omitir las tres opciones `BootstrapAdmin` para no
crearlo, o proporcionar las tres juntas. La contraseña debe cumplir las reglas
de Identity: ocho caracteres como mínimo, mayúscula, minúscula, número y símbolo.
La configuración incompleta detiene el arranque. La creación solo se intenta si
el correo no existe; no modifica contraseñas ni roles de cuentas existentes.
Retirar estas opciones después de crear el administrador.

El arranque crea los roles `ADMIN` y `USER` si faltan. Por ello, incluso sin
administrador inicial, necesita una base disponible y con el esquema aplicado.

## 2. Compilar, preparar la base y ejecutar

```powershell
dotnet restore .\Dsw2025Tpi.sln
dotnet build .\Dsw2025Tpi.sln --configuration Debug
dotnet test .\Dsw2025Tpi.sln --configuration Debug --no-build
```

Si `dotnet-ef` no está instalado, instalarlo con
`dotnet tool install --global dotnet-ef --version 9.0.7`. Si ya existe otra versión,
revisar su compatibilidad antes de modificarla.

Las migraciones deben aplicarse **explícitamente**: el arranque no ejecuta
`Database.Migrate()`. Revisar las migraciones antes de usar una base compartida.

```powershell
dotnet dev-certs https --trust
dotnet ef database update --project .\Dsw2025Tpi.Data\Dsw2025Tpi.Data.csproj --startup-project .\Dsw2025Tpi.Api\Dsw2025Tpi.Api.csproj
dotnet run --project .\Dsw2025Tpi.Api\Dsw2025Tpi.Api.csproj --launch-profile https
```

| Recurso | URL local |
| --- | --- |
| API HTTPS | `https://localhost:7138` |
| Perfil HTTP | `http://localhost:5142` |
| Swagger | `https://localhost:7138/swagger` |
| Comprobación básica | `https://localhost:7138/healthcheck` |

Swagger se habilita únicamente en `Development`; ambos perfiles locales usan
ese entorno. El healthcheck no comprueba SQL Server. Las rutas principales son
`/api/auth`, `/api/products` y `/api/orders`.

Para conectar el frontend, usar `http://localhost:5173`: es el origen exacto
permitido por CORS. `127.0.0.1` u otro puerto no son equivalentes.

## Verificación y limitaciones

Después de compilar en Debug:

```powershell
powershell -NoProfile -File .\test\Check-StartupConfiguration.ps1
```

- La solución no contiene proyectos de pruebas: `dotnet test` no ejecuta pruebas
  funcionales. Los JSON de `test/` son ejemplos manuales y algunos contienen comentarios.
- La comprobación PowerShell incluye nueve casos de configuración del arranque,
  sin conectarse a SQL Server ni crear cuentas. No prueba pedidos ni autenticación completa.
- Existen advertencias de nulabilidad y conflictos entre EF Core 9.0.6 y 9.0.7;
  una compilación exitosa no significa ausencia de advertencias.
- Es un proyecto académico con deuda funcional y de autorización pendiente,
  no una aplicación preparada para producción.

**Seguridad:** rotar cualquier clave JWT o credencial administrativa previamente
expuesta antes de reutilizar un entorno. Eliminarla del código no la revoca ni
elimina las copias históricas. No versionar secretos, certificados privados o
configuración local.
