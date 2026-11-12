<?xml version="1.0" encoding="utf-8"?>
<xsl:stylesheet xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
                xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                version="1.0">
  <xsl:output method="xml" indent="yes"/>
  <xsl:template match="/parameters">
    <w:document>
      <w:body>
        <w:p>
          <w:pPr><w:jc w:val="center"/></w:pPr>
          <w:r><w:rPr><w:b/></w:rPr><w:t xml:space="preserve">БЛАГОДАРСТВЕННОЕ ПИСЬМО</w:t></w:r>
        </w:p>
        <w:p/>
        <w:p>
          <w:r><w:rPr><w:b/></w:rPr><w:t xml:space="preserve"><xsl:value-of select="Counterparty_Name"/></w:t></w:r>
        </w:p>
        <w:p/>
        <w:p>
          <w:r><w:t xml:space="preserve">Выражаем искреннюю благодарность за многолетнее плодотворное сотрудничество и оперативность в обработке корреспонденции.</w:t></w:r>
        </w:p>
        <w:p>
          <w:r><w:t xml:space="preserve">Надеемся на дальнейшее развитие взаимовыгодных отношений.</w:t></w:r>
        </w:p>
        <w:p/>
        <w:p>
          <w:r><w:rPr></w:rPr><w:t xml:space="preserve"><xsl:value-of select="Counterparty_Position"/></w:t></w:r>
        </w:p>
        <w:p>
          <w:r><w:rPr></w:rPr><w:t xml:space="preserve"><xsl:value-of select="Counterparty_DirectorFio"/></w:t></w:r>
        </w:p>
      </w:body>
    </w:document>
  </xsl:template>
</xsl:stylesheet>
