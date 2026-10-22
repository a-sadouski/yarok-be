-- For a NEW empty database only. Existing Supabase data stays in place.
BEGIN;
CREATE TABLE categories(id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, name text NOT NULL, slug text NOT NULL UNIQUE);
CREATE TABLE colors(id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, name text NOT NULL, slug text NOT NULL UNIQUE, hex text);
CREATE TABLE products(id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, category_id bigint, name text NOT NULL, slug text NOT NULL UNIQUE, description text, is_variable boolean NOT NULL DEFAULT false, is_active boolean NOT NULL DEFAULT true, created_at timestamptz DEFAULT now());
CREATE TABLE product_variants(id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, product_id bigint NOT NULL, color_id bigint, price numeric(10,2) NOT NULL, old_price numeric(10,2), sku text, in_stock boolean NOT NULL DEFAULT true, is_active boolean NOT NULL DEFAULT true, UNIQUE(product_id,color_id));
CREATE TABLE product_images(id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY, product_id bigint NOT NULL, image_path text NOT NULL, sort_order integer DEFAULT 0);
COMMIT;
