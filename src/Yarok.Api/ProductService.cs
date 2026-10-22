using System.Globalization;
using System.Text.Json;
using Npgsql;

namespace Yarok.Api;

public sealed class ProductService(Database db, StorageService storage, IConfiguration config)
{
    public async Task<object> List(CancellationToken ct)
    {
        var products = await db.Query("""select id, category_id as "categoryId", name, slug, description, is_variable as "isVariable", is_active as "isActive", created_at as "createdAt" from products where is_active = true order by id""", ct);
        if (products.Count == 0) return products;
        var images = await db.Query("""select i.product_id as "productId", i.image_path as "imagePath" from product_images i join products p on p.id=i.product_id where p.is_active=true order by coalesce(i.sort_order,0), i.id""", ct);
        var variants = await db.Query("""select v.id, v.product_id as "productId", v.color_id as "colorId", v.price::text as price, v.old_price::text as "oldPrice", v.sku, v.in_stock as "inStock", v.is_active as "isActive" from product_variants v join products p on p.id=v.product_id where p.is_active=true order by v.id""", ct);
        var mainImages = images.GroupBy(i => i["productId"]!).ToDictionary(g => g.Key, g => g.First()["imagePath"]);
        var productVariants = variants.ToLookup(v => v["productId"]!);
        foreach (var p in products)
        {
            var image = mainImages.GetValueOrDefault(p["id"]!);
            p["imagePath"] = image;
            p["previewImage"] = image is null ? null : $"{Settings.Required(config, "SUPABASE_URL").TrimEnd('/')}/storage/v1/object/public/products-images/{image}";
            p["variants"] = productVariants[p["id"]!].ToArray();
        }
        return products;
    }

    public async Task<object> Save(HttpRequest request, long? id, CancellationToken ct)
    {
        if (!request.HasFormContentType) throw new ApiException(400, "Form data is empty");
        var form = await request.ReadFormAsync(ct);
        string Field(string key) => form[key].ToString().Trim();
        var name = Field("name");
        var slug = Field("slug");
        if (name.Length == 0 || slug.Length == 0) throw new ApiException(400, "Name and slug are required");
        if (!long.TryParse(Field("categoryId"), out var category) || category <= 0) throw new ApiException(400, "Category is required");
        bool BooleanField(string key, bool fallback)
        {
            var value = Field(key);
            if (value.Length == 0) return fallback;
            if (!bool.TryParse(value, out var parsed)) throw new ApiException(400, $"{key} must be true or false");
            return parsed;
        }
        var variable = BooleanField("isVariable", false);
        var active = BooleanField("isActive", true);
        List<VariantInput> variants;
        if (variable)
        {
            try { variants = JsonSerializer.Deserialize<List<VariantInput>>(Field("variants"), new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? []; }
            catch (JsonException) { throw new ApiException(400, "Variants JSON is invalid"); }
            if (variants.Count == 0) throw new ApiException(400, "Variants list is empty");
        }
        else
        {
            long? color = long.TryParse(Field("colorId"), out var colorId) ? colorId : null;
            if (Field("colorId").Length > 0 && color is null) throw new ApiException(400, "Valid color is required");
            if (id is null && color is null) throw new ApiException(400, "Color is required for non-variable product");
            variants = [new(color, Field("price"), Field("sku"), BooleanField("inStock", true))];
        }
        foreach (var v in variants)
        {
            if (v is null) throw new ApiException(400, "Invalid variant");
            if (!decimal.TryParse(v.Price, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var price) || price < 0 || price > 99999999.99m || decimal.Round(price, 2) != price)
                throw new ApiException(400, "Valid price with at most two decimals is required");
            if ((variable && v.ColorId is null) || v.ColorId <= 0) throw new ApiException(400, "Valid color is required");
        }
        if (variants.Where(v => v.ColorId.HasValue).GroupBy(v => v.ColorId).Any(g => g.Count() > 1)) throw new ApiException(400, "Duplicate variant color");
        var image = form.Files.GetFile("image");
        if (image is { Length: > 0 }) storage.Validate(image);
        await using var conn = await db.Open(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        if (id.HasValue)
        {
            await using var exists = new NpgsqlCommand("select id from products where id = $1 for update", conn, tx);
            exists.Parameters.AddWithValue(id.Value);
            if (await exists.ExecuteScalarAsync(ct) is null) throw new ApiException(404, "Product not found");
        }
        foreach (var lookup in new[] { (Table: "categories", Ids: new[] { category }), (Table: "colors", Ids: variants.Where(v => v.ColorId.HasValue).Select(v => v.ColorId!.Value).Distinct().ToArray()) })
        {
            await using var check = new NpgsqlCommand($"select count(*) from {lookup.Table} where id = any($1)", conn, tx);
            check.Parameters.AddWithValue(lookup.Ids);
            if ((long)(await check.ExecuteScalarAsync(ct))! != lookup.Ids.Length) throw new ApiException(400, $"Unknown {lookup.Table}");
        }
        var sql = id.HasValue
            ? "update products set name=$1, slug=$2, description=$3, category_id=$4, is_variable=$5, is_active=$6 where id=$7 returning id"
            : "insert into products(name,slug,description,category_id,is_variable,is_active) values($1,$2,$3,$4,$5,$6) returning id";
        await using var cmd = new NpgsqlCommand(sql, conn, tx);
        cmd.Parameters.AddWithValue(name); cmd.Parameters.AddWithValue(slug);
        cmd.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text, Value = string.IsNullOrEmpty(Field("description")) ? DBNull.Value : Field("description") });
        cmd.Parameters.AddWithValue(category); cmd.Parameters.AddWithValue(variable); cmd.Parameters.AddWithValue(active);
        if (id.HasValue) cmd.Parameters.AddWithValue(id.Value);
        var productId = (long)(await cmd.ExecuteScalarAsync(ct))!;
        await using var delete = new NpgsqlCommand("delete from product_variants where product_id=$1", conn, tx);
        delete.Parameters.AddWithValue(productId); await delete.ExecuteNonQueryAsync(ct);
        foreach (var v in variants)
        {
            await using var insert = new NpgsqlCommand("insert into product_variants(product_id,color_id,price,sku,in_stock,is_active) values($1,$2,$3,$4,$5,true)", conn, tx);
            insert.Parameters.AddWithValue(productId);
            insert.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Bigint, Value = (object?)v.ColorId ?? DBNull.Value });
            insert.Parameters.AddWithValue(decimal.Parse(v.Price!, CultureInfo.InvariantCulture));
            insert.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Text, Value = (object?)v.Sku ?? DBNull.Value });
            insert.Parameters.AddWithValue(v.InStock ?? true);
            await insert.ExecuteNonQueryAsync(ct);
        }
        string? imagePath = null;
        if (image is { Length: > 0 })
        {
            imagePath = await storage.Upload(productId, image, ct);
            await using var img = new NpgsqlCommand("""
                with main_image as (
                    select id from product_images where product_id=$1   
                    order by coalesce(sort_order,0),id limit 1
                ), updated as (
                    update product_images set image_path=$2
                    where id=(select id from main_image) returning id
                )
                insert into product_images(product_id,image_path,sort_order)
                select $1,$2,0 where not exists(select 1 from updated)
                """, conn, tx);
            img.Parameters.AddWithValue(productId); img.Parameters.AddWithValue(imagePath);
            await img.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return new { id = productId, name, slug, isVariable = variable, imagePath };
    }
}

public record VariantInput(long? ColorId, string? Price, string? Sku, bool? InStock);
