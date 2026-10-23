from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]

class MigrationStartupTests(unittest.TestCase):
    def test_request_4_same_scope_awaited_order(self):
        p = (ROOT / "Dsw2025Tpi.Api/Program.cs").read_text(encoding="utf-8-sig")
        self.assertIn("using Microsoft.EntityFrameworkCore;", p)
        self.assertRegex(p, r"using \(var scope = app.Services.CreateScope\(\)\)\s*\{\s*"
                         r"var services = scope.ServiceProvider;\s*"
                         r"var context = services.GetRequiredService<Dsw2025TpiContext>\(\);\s*"
                         r"await context.Database.MigrateAsync\(\);\s*"
                         r"var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>\(\);\s*"
                         r"var userManager = services.GetRequiredService<UserManager<IdentityUser>>\(\);")

    def test_requests_4_6_no_duplicate_or_ensure(self):
        sources = [p for p in ROOT.rglob("*.cs") if not {"bin", "obj", ".git"}.intersection(p.parts)]
        text = "\n".join(p.read_text(encoding="utf-8-sig") for p in sources)
        self.assertEqual(len(re.findall(r"\.Migrate(?:Async)?\s*\(", text)), 1)
        self.assertNotRegex(text, r"\.Ensure(?:Created|Deleted)(?:Async)?\s*\(")

    def test_request_3_existing_migration_tables(self):
        folder = ROOT / "Dsw2025Tpi.Data/Migrations"
        migration = folder / "20251201020512_UnificacionFinal.cs"
        up = migration.read_text(encoding="utf-8-sig").split("protected override void Down")[0]
        for table in ("Roles", "Usuarios", "Products"):
            with self.subTest(table=table):
                self.assertRegex(up, r'CreateTable\(\s*name: "' + table + '"')
        designer = migration.with_suffix(".Designer.cs").read_text(encoding="utf-8-sig")
        self.assertIn('[Migration("20251201020512_UnificacionFinal")]', designer)

if __name__ == "__main__":
    unittest.main()
