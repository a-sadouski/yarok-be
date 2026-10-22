-- Review and apply manually ONLY if the database still uses the old Drizzle migration.
BEGIN;
ALTER TABLE products ADD COLUMN IF NOT EXISTS is_variable boolean NOT NULL DEFAULT false;
ALTER TABLE product_variants ALTER COLUMN color_id DROP NOT NULL;
DO $$ BEGIN
  IF EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='product_images' AND column_name='image_url')
     AND NOT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public' AND table_name='product_images' AND column_name='image_path') THEN
    ALTER TABLE product_images RENAME COLUMN image_url TO image_path;
  END IF;
END $$;
-- If image_url contained full URLs, convert those values to bucket-relative paths separately.
COMMIT;
