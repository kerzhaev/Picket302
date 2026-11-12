-- Описание: клипарт blank_fields (функция 158)
-- S26 (функция 158): клипарт в произвольном макете blank.xml/blank_fields.
-- Идентификатор элемента библиотеки клипарта для picture-полей;
-- NULL — прежнее поведение (поле рисует логотип отправителя).
ALTER TABLE blank_fields ADD COLUMN clipart_id TEXT NULL;
