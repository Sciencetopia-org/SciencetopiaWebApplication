using Microsoft.EntityFrameworkCore;
using Sciencetopia.Data;
using Sciencetopia.Models.L10n;

namespace Sciencetopia.Services.L10n
{
    public class L10nMigrationReport
    {
        public int Nodes { get; set; }
        public int L10nSets { get; set; }
        public int L10nItems { get; set; }
        public int Exceptions { get; set; }
        public List<string> Errors { get; } = new();
    }

    public class L10nMigrationRunner
    {
        private readonly ApplicationDbContext _db;
        public L10nMigrationRunner(ApplicationDbContext db) { _db = db; }

        private static string? DetectLangCode(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            int cjk = 0, latin = 0, total = 0;
            foreach (var ch in text)
            {
                if (char.IsWhiteSpace(ch) || char.IsPunctuation(ch) || char.IsDigit(ch)) continue;
                total++;
                if ((ch >= 0x4E00 && ch <= 0x9FFF) || (ch >= 0x3400 && ch <= 0x4DBF) || (ch >= 0x20000 && ch <= 0x2A6DF)) cjk++;
                else if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z')) latin++;
            }
            if (total == 0) return null;
            if (cjk * 100 / total >= 30) return "zh";
            if (latin * 100 / total >= 30) return "en";
            return null;
        }

        private async Task<bool> TableExistsAsync(string table, CancellationToken ct)
        {
            var cs = _db.Database.GetConnectionString();
            if (string.IsNullOrWhiteSpace(cs)) return false;
            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cs);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM sys.objects WHERE object_id = OBJECT_ID(@t) AND type='U'";
            var p = cmd.CreateParameter(); p.ParameterName = "@t"; p.Value = table; cmd.Parameters.Add(p);
            var res = await cmd.ExecuteScalarAsync(ct);
            return res != null && res != DBNull.Value;
        }

        private async Task<List<(string Language, string? Name, string? Description)>> GetNodeTranslationsAsync(Guid nodeId, CancellationToken ct)
        {
            var list = new List<(string, string?, string?)>();
            if (!await TableExistsAsync("dbo.KnowledgeNodeTranslations", ct)) return list;
            var cs = _db.Database.GetConnectionString();
            if (string.IsNullOrWhiteSpace(cs)) return list;
            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cs);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Language, Name, Description FROM dbo.KnowledgeNodeTranslations WHERE NodeId=@id";
            var p = cmd.CreateParameter(); p.ParameterName = "@id"; p.Value = nodeId; cmd.Parameters.Add(p);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                var lang = rd.IsDBNull(0) ? "" : rd.GetString(0);
                var name = rd.IsDBNull(1) ? null : rd.GetString(1);
                var desc = rd.IsDBNull(2) ? null : rd.GetString(2);
                list.Add((lang, name, desc));
            }
            return list;
        }

        private async Task<List<(string Language, string? Name, string? Description)>> GetTagTranslationsAsync(Guid tagId, CancellationToken ct)
        {
            var list = new List<(string, string?, string?)>();
            if (!await TableExistsAsync("dbo.TagTranslations", ct)) return list;
            var cs = _db.Database.GetConnectionString();
            if (string.IsNullOrWhiteSpace(cs)) return list;
            await using var conn = new Microsoft.Data.SqlClient.SqlConnection(cs);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Language, Name, Description FROM dbo.TagTranslations WHERE TagId=@id";
            var p = cmd.CreateParameter(); p.ParameterName = "@id"; p.Value = tagId; cmd.Parameters.Add(p);
            await using var rd = await cmd.ExecuteReaderAsync(ct);
            while (await rd.ReadAsync(ct))
            {
                var lang = rd.IsDBNull(0) ? "" : rd.GetString(0);
                var name = rd.IsDBNull(1) ? null : rd.GetString(1);
                var desc = rd.IsDBNull(2) ? null : rd.GetString(2);
                list.Add((lang, name, desc));
            }
            return list;
        }

        public async Task<L10nMigrationReport> MigrateFromLegacyAsync(bool dryRun = true, CancellationToken ct = default)
        {
            var report = new L10nMigrationReport();
            var connStr = _db.Database.GetDbConnection().ConnectionString;
            if (string.IsNullOrWhiteSpace(connStr))
            {
                throw new InvalidOperationException("Missing ConnectionStrings:DefaultConnection (DbContext has empty connection string). Ensure the appsettings/appsettings.{ENV}.json or environment variable ConnectionStrings__DefaultConnection is populated in this process.");
            }
            var nodes = await _db.KnowledgeNodes.Where(n => n.Id != null).ToListAsync(ct);
            report.Nodes = nodes.Count;

            foreach (var node in nodes)
            {
                try
                {
                    if (!node.DefaultL10nSetId.HasValue)
                    {
                        var set = new L10nSet { Scope = "knowledge_node" };
                        if (!dryRun) _db.L10nSets.Add(set);
                        if (!dryRun) await _db.SaveChangesAsync(ct);
                        if (!dryRun)
                        {
                            node.DefaultL10nSetId = set.L10nSetId;
                            _db.NodeL10nSets.Add(new NodeL10nSet { NodeId = node.Id!.Value, L10nSetId = set.L10nSetId, Relation = 0 });
                            await _db.SaveChangesAsync(ct);
                        }
                    }

                    var setId = node.DefaultL10nSetId ?? Guid.Empty; // if dry-run and just created, this will be empty

                    var translations = await GetNodeTranslationsAsync(node.Id!.Value, ct);

                    foreach (var t in translations)
                    {
                        var item = new L10nItem
                        {
                            FieldKey = "name",
                            LangCode = t.Language,
                            Kind = L10nItemKind.Primary,
                            Text = t.Name,
                            SortOrder = 0
                        };
                        if (!dryRun)
                        {
                            _db.L10nItems.Add(item);
                            await _db.SaveChangesAsync(ct);
                            _db.L10nSetItems.Add(new L10nSetItem { L10nSetId = setId, L10nItemId = item.L10nItemId });
                        }
                        report.L10nItems++;

                        // description if exists
                        if (!string.IsNullOrWhiteSpace(t.Description))
                        {
                            var dItem = new L10nItem
                            {
                                FieldKey = "description",
                                LangCode = t.Language,
                                Kind = L10nItemKind.Primary,
                                Content = t.Description,
                                SortOrder = 0
                            };
                            if (!dryRun)
                            {
                                _db.L10nItems.Add(dItem);
                                await _db.SaveChangesAsync(ct);
                                _db.L10nSetItems.Add(new L10nSetItem { L10nSetId = setId, L10nItemId = dItem.L10nItemId });
                            }
                            report.L10nItems++;
                        }
                    }

                    // also migrate base name/description with detected language
                    if (!string.IsNullOrWhiteSpace(node.Name))
                    {
                        var baseLang = DetectLangCode(node.Name);
                        var item = new L10nItem
                        {
                            FieldKey = "name",
                            LangCode = baseLang,
                            Kind = L10nItemKind.Primary,
                            Text = node.Name,
                            SortOrder = 0
                        };
                        if (!dryRun)
                        {
                            _db.L10nItems.Add(item);
                            await _db.SaveChangesAsync(ct);
                            _db.L10nSetItems.Add(new L10nSetItem { L10nSetId = setId, L10nItemId = item.L10nItemId });
                        }
                        report.L10nItems++;
                    }

                    if (!string.IsNullOrWhiteSpace(node.Description))
                    {
                        var baseLang = DetectLangCode(node.Description);
                        var dItem = new L10nItem
                        {
                            FieldKey = "description",
                            LangCode = baseLang,
                            Kind = L10nItemKind.Primary,
                            Content = node.Description,
                            SortOrder = 0
                        };
                        if (!dryRun)
                        {
                            _db.L10nItems.Add(dItem);
                            await _db.SaveChangesAsync(ct);
                            _db.L10nSetItems.Add(new L10nSetItem { L10nSetId = setId, L10nItemId = dItem.L10nItemId });
                        }
                        report.L10nItems++;
                    }

                    if (!dryRun) await _db.SaveChangesAsync(ct);
                    report.L10nSets++;
                }
                catch (Exception ex)
                {
                    report.Exceptions++;
                    report.Errors.Add($"Node {node.Id}: {ex.Message}");
                }
            }

            // Tags migration
            var tags = await _db.Tags.Where(t => t.Id != null).ToListAsync(ct);
            foreach (var tag in tags)
            {
                try
                {
                    if (!tag.DefaultL10nSetId.HasValue)
                    {
                        var set = new L10nSet { Scope = "tag" };
                        if (!dryRun) _db.L10nSets.Add(set);
                        if (!dryRun) await _db.SaveChangesAsync(ct);
                        if (!dryRun)
                        {
                            tag.DefaultL10nSetId = set.L10nSetId;
                            _db.TagL10nSets.Add(new TagL10nSet { TagId = tag.Id!.Value, L10nSetId = set.L10nSetId, Relation = 0 });
                            await _db.SaveChangesAsync(ct);
                        }
                    }
                    var setId = tag.DefaultL10nSetId ?? Guid.Empty;

                    var translations = await GetTagTranslationsAsync(tag.Id!.Value, ct);
                    foreach (var tr in translations)
                    {
                        if (!string.IsNullOrWhiteSpace(tr.Name))
                        {
                            var tItem = new L10nItem { FieldKey = "name", LangCode = tr.Language, Kind = L10nItemKind.Primary, Text = tr.Name };
                            if (!dryRun)
                            {
                                _db.L10nItems.Add(tItem);
                                await _db.SaveChangesAsync(ct);
                                _db.L10nSetItems.Add(new L10nSetItem { L10nSetId = setId, L10nItemId = tItem.L10nItemId });
                            }
                            report.L10nItems++;
                        }
                        if (!string.IsNullOrWhiteSpace(tr.Description))
                        {
                            var dItem = new L10nItem { FieldKey = "description", LangCode = tr.Language, Kind = L10nItemKind.Primary, Content = tr.Description };
                            if (!dryRun)
                            {
                                _db.L10nItems.Add(dItem);
                                await _db.SaveChangesAsync(ct);
                                _db.L10nSetItems.Add(new L10nSetItem { L10nSetId = setId, L10nItemId = dItem.L10nItemId });
                            }
                            report.L10nItems++;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(tag.Name))
                    {
                        var lang = DetectLangCode(tag.Name);
                        var tItem = new L10nItem { FieldKey = "name", LangCode = lang, Kind = L10nItemKind.Primary, Text = tag.Name };
                        if (!dryRun)
                        {
                            _db.L10nItems.Add(tItem);
                            await _db.SaveChangesAsync(ct);
                            _db.L10nSetItems.Add(new L10nSetItem { L10nSetId = setId, L10nItemId = tItem.L10nItemId });
                        }
                        report.L10nItems++;
                    }
                    if (!string.IsNullOrWhiteSpace(tag.Description))
                    {
                        var lang = DetectLangCode(tag.Description);
                        var dItem = new L10nItem { FieldKey = "description", LangCode = lang, Kind = L10nItemKind.Primary, Content = tag.Description };
                        if (!dryRun)
                        {
                            _db.L10nItems.Add(dItem);
                            await _db.SaveChangesAsync(ct);
                            _db.L10nSetItems.Add(new L10nSetItem { L10nSetId = setId, L10nItemId = dItem.L10nItemId });
                        }
                        report.L10nItems++;
                    }
                }
                catch (Exception ex)
                {
                    report.Exceptions++;
                    report.Errors.Add($"Tag {tag.Id}: {ex.Message}");
                }
            }

            return report;
        }
    }
}
